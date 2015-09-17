﻿using GoodAI.Core.Execution;
using GoodAI.Core.Nodes;
using GoodAI.Core.Task;
using GoodAI.Core.Utils;
using GoodAI.Modules.LSTM.Tasks;
using GoodAI.Modules.NeuralNetwork.Layers;
using GoodAI.Modules.NeuralNetwork.Tasks;
using GoodAI.Modules.RBM;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using YAXLib;

namespace GoodAI.Modules.NeuralNetwork.Group
{
    /// <author>GoodAI</author>
    /// <meta>ph</meta>
    /// <status>Working</status>
    /// <summary>Network node group.</summary>
    /// <description>
    /// The Neural Network Group is necessary to build a neural network consisting of neural layers.<br></br>
    /// It is required to control the data flow during feed-forward and backpropagation between layers, as well as holding important hyperparameters and method variables.
    /// </description>
    public class MyNeuralNetworkGroup : MyNodeGroup, IMyCustomExecutionPlanner
    {
        //Node properties
        [YAXSerializableField(DefaultValue = 0.0f)]
        [MyBrowsable, Category("\tRegularization")]
        public float L1 { get; set; }

        [YAXSerializableField(DefaultValue = 0.0f)]
        [MyBrowsable, Category("\tRegularization")]
        public float L2 { get; set; }

        [YAXSerializableField(DefaultValue = 0.0f)]
        [MyBrowsable, Category("\tRegularization")]
        public float Dropout { get; set; }

        //Memory Blocks
        public List<MyNode> SortedChildren;
        public MyAbstractLayer FirstLayer;
        internal MyAbstractLayer LastLayer;
        internal int TotalWeights;

        //Tasks
        [MyTaskGroup("BackPropagation")]
        public MySGDTask SGD { get; protected set; }
        [MyTaskGroup("BackPropagation")]
        public MyRMSTask RMS { get; protected set; }
        [MyTaskGroup("BackPropagation")]
        public MyAdadeltaTask Adadelta { get; protected set; }

        public MyAbstractBackpropTask GetActiveBackpropTask() {
            if (SGD.Enabled)
                return SGD;
            if (RMS.Enabled)
                return RMS;
            if (Adadelta.Enabled)
                return Adadelta;
            return null;
        }

        //[MyTaskGroup("BackPropagation")]
        //public MyvSGDfdTask vSGD { get; protected set; }

        public MyInitNNGroupTask InitGroup { get; protected set; }
        public MyGradientCheckTask GradientCheck { get; protected set; }

        public MyNeuralNetworkGroup()
        {
            InputBranches = 2; // usually 2 inputs (input, target or input, reward)
            OutputBranches = 1; // usually 1 output (output or action)
        }  //parameterless constructor

        //Memory blocks size rules
        public override void UpdateMemoryBlocks()
        {
            base.UpdateMemoryBlocks();
        }

        private List<IMyExecutable> GetTasks(MyWorkingNode node)
        {
            List<IMyExecutable> tasks = new List<IMyExecutable>();

            foreach (string taskName in node.GetInfo().KnownTasks.Keys)
            {
                MyTask task = node.GetTaskByPropertyName(taskName);
                tasks.Add(task);
            }

            MyNodeGroup nodeGroup = node as MyNodeGroup;
            if (nodeGroup != null)
            {
                foreach (MyNode childNode in nodeGroup.Children)
                {
                    MyWorkingNode childWorkingNode = childNode as MyWorkingNode;
                    if (childWorkingNode != null)
                    {
                        tasks.AddRange(GetTasks(childWorkingNode));
                    }
                }
            }

            return tasks;
        }

        public override void Validate(MyValidator validator)
        {
            base.Validate(validator);

            List<IMyExecutable> tasks = GetTasks(this);
            
            validator.AssertError(tasks.Find(task => task is IMyForwardTask) != null, this, "You need to have at least one forward task");
        }

        public virtual MyExecutionBlock CreateCustomInitPhasePlan(MyExecutionBlock defaultInitPhasePlan)
        {
            return defaultInitPhasePlan;
        }

        public virtual MyExecutionBlock CreateCustomExecutionPlan(MyExecutionBlock defaultPlan)
        {
            List<IMyExecutable> selected = new List<IMyExecutable>();
            List<IMyExecutable> newPlan = new List<IMyExecutable>();

            // copy default plan content to new plan content
            foreach (IMyExecutable groupTask in defaultPlan.Children)
                if (groupTask is MyExecutionBlock)
                    foreach (IMyExecutable nodeTask in (groupTask as MyExecutionBlock).Children)
                        newPlan.Add(nodeTask); // add individual node tasks
                else
                    newPlan.Add(groupTask); // add group tasks

            // remove group backprop tasks (they should be called from the individual layers)
            // DO NOT remove RBM tasks
            // DO NOT remove the currently selected backprop task (it handles batch learning)
            selected = newPlan.Where(task => task is MyAbstractBackpropTask &&  !(task.Enabled) && !(task is MyRBMLearningTask || task is MyRBMReconstructionTask)).ToList();
            newPlan.RemoveAll(selected.Contains);

            // move MyCreateDropoutMaskTask(s) before the first MyForwardTask
            selected = newPlan.Where(task => task is MyCreateDropoutMaskTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            newPlan.InsertRange(newPlan.IndexOf(newPlan.Find(task => task is IMyForwardTask)), selected);

            // move reversed MyOutputDeltaTask(s) after the last MyForwardTask (usually there is only one)
            selected = newPlan.Where(task => task is IMyOutputDeltaTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            if ((selected.Where(task => task.Enabled)).Count() > 1)
                MyLog.WARNING.WriteLine("More than one output tasks are active!");
            if (selected.Count <= 0)
                MyLog.WARNING.WriteLine("No output tasks are active! Planning (of SGD, RMS, Adadelta etc.) might not work properly. Possible cause: no output layer is present.\nIgnore this if RBM task is currently selected.");
            selected.Reverse();
            
            newPlan.InsertRange(newPlan.IndexOf(newPlan.FindLast(task => task is IMyForwardTask)) + 1, selected);

            // move reversed MyDeltaTask(s) after the last MyOutputDeltaTask
            selected = newPlan.Where(task => task is IMyDeltaTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            selected.Reverse();
            newPlan.InsertRange(newPlan.IndexOf(newPlan.FindLast(task => task is IMyOutputDeltaTask)) + 1, selected);


            // move MyGradientCheckTask after the last MyDeltaTask
            selected = newPlan.Where(task => task is MyGradientCheckTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            newPlan.InsertRange(newPlan.IndexOf(newPlan.FindLast(task => task is IMyDeltaTask)) + 1, selected);

            // move currently selected backprop task between Delta tasks and UpdateWeights task
            selected = newPlan.Where(task => task is MyAbstractBackpropTask && (task.Enabled)).ToList();
            if (selected.Count > 1)
                MyLog.WARNING.WriteLine("Two or more backprop tasks selected.");
            if (selected.Count <= 0)
                MyLog.WARNING.WriteLine("No backprop task selected.");
            newPlan.RemoveAll(selected.Contains);
            selected.Reverse();
            newPlan.InsertRange(newPlan.IndexOf(newPlan.FindLast(task => task is IMyDeltaTask)) + 1, selected);



            // move MyUpdateWeightsTask(s) after the last MyGradientCheckTask
            selected = newPlan.Where(task => task is IMyUpdateWeightsTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            newPlan.InsertRange(newPlan.IndexOf(newPlan.FindLast(task => task is MyGradientCheckTask)) + 1, selected);

            // move MyQLearningTask after the last MyForwardTask
            selected = newPlan.Where(task => task is MyQLearningTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            newPlan.InsertRange(newPlan.IndexOf(newPlan.FindLast(task => task is IMyForwardTask)) + 1, selected);

            // move MyRestoreValuesTask after the last MyAbstractBackPropTask
            selected = newPlan.Where(task => task is MyRestoreValuesTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            newPlan.InsertRange(newPlan.IndexOf(newPlan.FindLast(task => task is IMyUpdateWeightsTask)) + 1, selected);

            // move MyLSTMPartialDerivativesTask after the last MyForwardTask
            selected = newPlan.Where(task => task is MyLSTMPartialDerivativesTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            newPlan.InsertRange(newPlan.IndexOf(newPlan.FindLast(task => task is IMyForwardTask)) + 1, selected);

            // move MySaveActionTask to the end of the task list
            selected = newPlan.Where(task => task is MySaveActionTask).ToList();
            newPlan.RemoveAll(selected.Contains);
            newPlan.AddRange(selected);

            // return new plan as MyExecutionBlock
            return new MyExecutionBlock(newPlan.ToArray());
        }

        public void FeedForward()
        {
            MyAbstractLayer layer = FirstLayer;
            while (layer != null)
            {
                layer.ForwardTask.Execute();
                layer = layer.NextLayer;
            }
        }

        public float GetError()
        {
            // get the error from output layer
            if (LastLayer is MyAbstractOutputLayer)
            {
                // pointer to output layer
                MyAbstractOutputLayer outputLayer = LastLayer as MyAbstractOutputLayer;

                // get enabled loss function
                MyTask lossTask = outputLayer.GetEnabledTask("LossFunctions");

                // no loss function?
                if (lossTask == null)
                {
                    // Get call stack
                    StackTrace stackTrace = new StackTrace();

                    MyLog.ERROR.WriteLine("ERROR: GetError() called from " + stackTrace.GetFrame(1).GetMethod().Name + " needs a LossFunction task to be selected in the OutputLayer.");
                    return 0.0f;
                }

                // execute loss function
                lossTask.Execute();

                // copy to host
                outputLayer.Cost.SafeCopyToHost();

                // return cost (error)
                return outputLayer.Cost.Host[0];
            }
            else
            {
                // Get call stack
                StackTrace stackTrace = new StackTrace();

                MyLog.ERROR.WriteLine("ERROR: GetError() called from " + stackTrace.GetFrame(1).GetMethod().Name + " needs an OutputLayer as the last layer.");
                return 0.0f;
            }
        }

        // handles batch learning
        // should be called after every backward pass
        public void NextSample()
        {
            if (GetActiveBackpropTask() != null)
                GetActiveBackpropTask().BatchIndex++;
        }

        // are we at the beginning of a new batch - should we reset deltas?
        public bool NewBatch()
        {
            return (GetActiveBackpropTask() != null) && (GetActiveBackpropTask().BatchIndex == 0);
        }
    }
}