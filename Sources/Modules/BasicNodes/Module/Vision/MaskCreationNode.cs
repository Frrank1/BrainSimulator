﻿using GoodAI.Core;
using GoodAI.Core.Memory;
using GoodAI.Core.Nodes;
using GoodAI.Core.Observers; // Because of the keyboard...
using GoodAI.Core.Task;
using GoodAI.Core.Utils;
using GoodAI.Modules.Transforms;
using GoodAI.Modules.Vision;
using System;
using System.ComponentModel;
using YAXLib;

namespace GoodAI.Modules.Vision
{
    /// <author>GoodAI</author>
    /// <meta>jk</meta>
    /// <status>Working</status>
    /// <summary>
    /// ?
    /// </summary>
    /// <description> ? </description>
    public class MaskCreationNode : MyWorkingNode
    {

        //----------------------------------------------------------------------------
        // :: MEMORY BLOCKS ::
        [MyInputBlock(0)]
        public MyMemoryBlock<float> Image
        {
            get { return GetInput(0); }
        }

        [MyInputBlock(1)]
        public MyMemoryBlock<float> XCrop
        {
            get { return GetInput(1); }
        }

        [MyInputBlock(2)]
        public MyMemoryBlock<float> YCrop
        {
            get { return GetInput(2); }
        }


        [MyOutputBlock(0)]
        public MyMemoryBlock<float> Output
        {
            get { return GetOutput(0); }
            set { SetOutput(0, value); }
        }

        [MyOutputBlock(1)]
        public MyMemoryBlock<float> MaskedImageOutput
        {
            get { return GetOutput(1); }
            set { SetOutput(1, value); }
        }

        //----------------------------------------------------------------------------
        // :: INITS  ::
        public override void UpdateMemoryBlocks()
        {
            int dim0 = (Image != null && Image.Dims.Rank >= 1) ? Image.Dims[0] : 1;
            int dim1 = (Image != null && Image.Dims.Rank >= 2) ? Image.Dims[1] : 1;
            int dim2 = (Image != null && Image.Dims.Rank >= 3) ? Image.Dims[2] : 1;

            if (Image.Dims.Rank < 3)
            {
                Output.Dims = new TensorDimensions(dim0, dim1);
                MaskedImageOutput.Dims = new TensorDimensions(dim0, dim1);
            }
            else
            {
                Output.Dims = new TensorDimensions(dim0, dim1, dim2);
                MaskedImageOutput.Dims = new TensorDimensions(dim0, dim1, dim2);
            }
        }

        public override void Validate(MyValidator validator)
        {
            //base.Validate(validator); /// base checking 
            validator.AssertError(Image != null, this, "No input image available");
            validator.AssertError(Image.Dims.Rank >= 2, this, "Input image should have rank at least 2 (2 dimensions)");
        }

        public MaskCreationExecuteTask Execute { get; private set; }

        [Description("Execute")]
        public class MaskCreationExecuteTask : MyTask<MaskCreationNode>
        {
            MyCudaKernel kerX, kerY;
            MyCudaKernel m_multElementwiseKernel;

            public override void Init(int nGPU)
            {
                kerX = MyKernelFactory.Instance.Kernel(nGPU, @"Vision\VisionMath", "SetMatrixVauleMinMaxX");
                kerX.SetupExecution(Owner.Output.Count);

                kerY = MyKernelFactory.Instance.Kernel(nGPU, @"Vision\VisionMath", "SetMatrixVauleMinMaxY");
                kerY.SetupExecution(Owner.Output.Count);

                m_multElementwiseKernel = MyKernelFactory.Instance.KernelVector(Owner.GPU, KernelVector.ElementwiseMult);
                m_multElementwiseKernel.SetupExecution(Owner.Output.Count);
            }

            private bool CropHasUsefullValueAndCopy2Host(MyMemoryBlock<float> Crop)
            {
                if (Crop == null)
                    return false;
                Crop.SafeCopyToHost();
                // deadband aroud zero
                if (Crop.Host[0] < 0.1f && Crop.Host[0] > -0.1f)
                    return false;
                return true;
            }

            public override void Execute()
            {
                Owner.Output.Fill(1.0f);

                if (CropHasUsefullValueAndCopy2Host(Owner.XCrop))
                {
                    if (Owner.XCrop.Host[0] > 0f)
                    {
                        kerX.Run(Owner.Output, Owner.Output.Dims[0], Owner.Output.Count, 0, (int)(Owner.XCrop.Host[0] * Owner.Output.Dims[0]), 0f);
                    }
                    else
                    {
                        kerX.Run(Owner.Output, Owner.Output.Dims[0], Owner.Output.Count, (int)Owner.Output.Dims[0] + (int)(Owner.XCrop.Host[0] * Owner.Output.Dims[0]), (int)Owner.Output.Dims[0], 0f);
                    }
                }
                if (CropHasUsefullValueAndCopy2Host(Owner.YCrop))
                {
                    if (Owner.YCrop.Host[0] > 0f)
                    {
                        kerY.Run(Owner.Output, Owner.Output.Dims[0], Owner.Output.Count, 0, (int)(Owner.YCrop.Host[0] * Owner.Output.Dims[1]), 0f);
                    }
                    else
                    {
                        kerY.Run(Owner.Output, Owner.Output.Dims[0], Owner.Output.Count, (int)Owner.Output.Dims[1] + (int)(Owner.YCrop.Host[0] * Owner.Output.Dims[1]), (int)Owner.Output.Dims[1], 0f);
                    }
                }

                m_multElementwiseKernel.Run(
                    Owner.Image,
                    Owner.Output,
                    Owner.MaskedImageOutput,
                    Owner.Image.Count
                    );
            }
        }
    }
}