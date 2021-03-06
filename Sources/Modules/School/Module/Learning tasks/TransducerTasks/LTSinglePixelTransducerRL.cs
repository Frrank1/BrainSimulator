﻿using GoodAI.Core.Utils;
using GoodAI.Modules.School.Common;
using GoodAI.Modules.School.Worlds;
using GoodAI.ToyWorld;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;

namespace GoodAI.Modules.School.LearningTasks.TransducerTasks
{
    /// <summary>
    /// Abstract base class for finite-transducer-based School exercises.
    /// </summary>
    public abstract class LTSinglePixelTransducerRL : AbstractLearningTask<RoguelikeWorld>
    {
        protected FiniteTransducer m_ft;

        protected FiniteTransducerTransition m_lastTransition;
        protected bool m_stepIsCorrect;
        protected int m_importantActionsTaken;
        protected int m_importantActionsToTake;
        protected HashSet<int> m_importantActions;

        // graphics
        protected Dictionary<int, int> m_symbolColor;
        protected GameObject m_currentObject;
        protected Random m_rnd;

        public LTSinglePixelTransducerRL() : this(null) { }

        public LTSinglePixelTransducerRL(SchoolWorld w)
            : base(w)
        {
            m_symbolColor = new Dictionary<int, int>();
            m_symbolColor[0] = 0; // action 0: white color
            m_symbolColor[1] = 1; // action 1: black color
            m_symbolColor[2] = 2; // action 3: gray color

            m_rnd = new Random();

            m_currentObject = null;

            TSHints = new TrainingSetHints {
                { TSHintAttributes.IS_VARIABLE_COLOR, 0 },
                { TSHintAttributes.MAX_NUMBER_OF_ATTEMPTS, 10000 }
            };

            TSProgression.Add(TSHints.Clone());
            //TSProgression.Add(TSHintAttributes.IS_VARIABLE_COLOR, 1);
        }

        public override void Init()
        {
            m_importantActions = new HashSet<int>();

            CreateTransducer();
            m_ft.Start();
            m_stepIsCorrect = true;
            m_currentObject = null;
            m_lastTransition = null;

            base.Init();
        }

        public abstract void CreateTransducer();

        public override int NumberOfSuccessesRequired
        {
            get { return 20; }
        }

        // the school framework will paint the FOV black before PresentNewTrainingUnit is called - I need to re-paint
        // what ExecuteStepAfterEvaluation painted into the FOV.
        public override void PresentNewTrainingUnit()
        {
            m_importantActionsToTake = 1;
            m_importantActionsTaken = 0;
            if(m_lastTransition == null) // true for the very first training unit
            {
                FiniteTransducerTransition t = m_ft.pickNextTransitionRandomly();
                m_ft.UseTransition(t);
                m_lastTransition = t;
            }
            DisplayLastTransitionSymbol();
        }

        public override void ExecuteStepAfterEvaluation()
        {
            if (m_currentObject != null)
                WrappedWorld.RemoveGameObject(m_currentObject);

            FiniteTransducerTransition t = m_ft.pickNextTransitionRandomly();
            m_ft.UseTransition(t);
            m_lastTransition = t;

            DisplayLastTransitionSymbol();
        }

        void DisplayLastTransitionSymbol()
        {
            CreateObject(GetSymbolColor(m_lastTransition.symbol));
        }

        protected void CreateObject(int color)
        {
            m_currentObject = new Shape(Shape.Shapes.Square, new PointF(0f, 0f), new SizeF(WrappedWorld.Scene.Width, WrappedWorld.Scene.Height));
            WrappedWorld.AddGameObject(m_currentObject);

            SetObjectColor(color);
        }

        protected void SetObjectColor(int colorIndex)
        {
            m_currentObject.IsBitmapAsMask = true;

            Color color = LearningTaskHelpers.GetVisibleGrayscaleColor(colorIndex);
            m_currentObject.ColorMask = Color.FromArgb(
                AddRandomColorOffset(color.R),
                AddRandomColorOffset(color.G),
                AddRandomColorOffset(color.B));
        }

        protected byte AddRandomColorOffset(byte colorComponent)
        {
            if (TSHints[TSHintAttributes.IS_VARIABLE_COLOR] != 1.0f)
                return colorComponent;
            const int MAX_RANDOM_OFFSET = 10;
            return (byte)Math.Max(0, Math.Min(255, colorComponent + m_rnd.Next(-MAX_RANDOM_OFFSET, MAX_RANDOM_OFFSET + 1)));
        }

        protected int GetSymbolColor(int symbol)
        {
            return m_symbolColor[symbol];
        }
           
        public void EvaluateSinglePixelRLStep()
        {
            SchoolWorld.ActionInput.SafeCopyToHost();

            int action = 0;
            if (SchoolWorld.ActionInput.Host[ControlMapper.Idx("forward")] != 0)
                action = 1;
            else if (SchoolWorld.ActionInput.Host[ControlMapper.Idx("backward")] != 0)
                action = 2;
            else if (SchoolWorld.ActionInput.Host[ControlMapper.Idx("left")] != 0)
                action = 3;
            else if (SchoolWorld.ActionInput.Host[ControlMapper.Idx("right")] != 0)
                action = 4;

            int expectedAction = 0;
            if (m_lastTransition != null)
            {
                expectedAction = m_lastTransition.action;
                //MyLog.WARNING.WriteLine("Action taken: " + action+" expected action: "+expectedAction);
            }

            if (m_lastTransition == null)
            {
                m_stepIsCorrect = true;
                WrappedWorld.Reward.Host[0] = 0f;
            }
            else if(action == expectedAction)
            {
                m_stepIsCorrect = true;
                if (m_importantActions.Contains(action))
                {
                    m_importantActionsTaken++;
                    WrappedWorld.Reward.Host[0] = 1f;
                }
                else
                {
                    WrappedWorld.Reward.Host[0] = 0f;
                }
            }
            else
            {
                m_stepIsCorrect = false;
                WrappedWorld.Reward.Host[0] = -1f;
            }
        }

        protected override bool DidTrainingUnitComplete(ref bool wasUnitSuccessful)
        {
            EvaluateSinglePixelRLStep();

            if(m_stepIsCorrect && m_importantActionsTaken >= m_importantActionsToTake)
            {
                wasUnitSuccessful = true;
                return true;
            }

            if(!m_stepIsCorrect)
            {
                wasUnitSuccessful = false;
                return true;
            }

            return false;
        }

    }
}
