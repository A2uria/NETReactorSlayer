using System;
using System.Collections.Generic;
using de4dot.blocks;
using de4dot.blocks.cflow;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace GotoDeobfuscator
{
    public class ControlFlowReactorDo
    {
        private static BlocksCflowDeobfuscator CfDeob;
        public static Blocks cblocks;

        public static List<Block> methblocks;

        private static void InternalDeobfuscateCflow(MethodDef meth)
        {
            try
            {
                CfDeob = new BlocksCflowDeobfuscator();
                cblocks = new Blocks(meth);
                cblocks.RemoveDeadBlocks();
                cblocks.RepartitionBlocks();

                cblocks.UpdateBlocks();
                cblocks.Method.Body.SimplifyBranches();
                cblocks.Method.Body.OptimizeBranches();
                CfDeob.Initialize(cblocks);

                ControlFlowReactor cflow = new ControlFlowReactor();
                methblocks = cblocks.MethodBlocks.GetAllBlocks();
                CfDeob.Add(cflow);

                CfDeob.Deobfuscate();
                cblocks.RepartitionBlocks();

                IList<Instruction> instructions;
                IList<ExceptionHandler> exceptionHandlers;

                cblocks.GetCode(out instructions, out exceptionHandlers);

                DotNetUtils.RestoreBody(meth, instructions, exceptionHandlers);
            }
            catch { }
        }

        public static int FixCount = 0;

        public static void deobfuscate(MethodDef method)
        {
            if (method.Body == null)
                return;
            if (!method.HasBody)
                return;
            if (!method.Body.HasInstructions)
                return;

            InternalDeobfuscateCflow(method);
        }

        public static void deobfuscate(TypeDef type)
        {
            foreach (MethodDef method in type.Methods)
            {
                deobfuscate(method);
            }

            foreach (TypeDef ntype in type.NestedTypes)
            {
                deobfuscate(ntype);
            }
        }

        public static void deobfuscate(ModuleDefMD module)
        {
            FixCount = 0;
            foreach (TypeDef type in module.GetTypes())
            {
                deobfuscate(type);
            }
        }
    }

    public class ControlFlowReactor : BlockDeobfuscator
    {
        public ControlFlowReactor() { }

        public bool modified = false;

        public Block GetBlock(Instr ins)
        {
            foreach (Block block in ControlFlowReactorDo.methblocks)
            {
                if (block.FirstInstr == ins)
                    return block;
            }

            return null;
        }

        public Block GetBlock(Instruction ins)
        {
            foreach (Block block in ControlFlowReactorDo.methblocks)
            {
                if (block.FirstInstr.Instruction == ins)
                    return block;
            }

            return null;
        }

        protected override bool Deobfuscate(Block block)
        {
            if (block == null)
                return false;

            modified = false;
            if (
                block.Instructions.Count >= 2
                && block.Instructions[block.Instructions.Count - 2].IsLdcI4()
                && block.LastInstr.IsStloc()
            )
            {
                if (
                    block.FallThrough != null
                    && block.FallThrough.Instructions.Count == 3
                    && block.FallThrough.FirstInstr.IsLdloc()
                    && block.FallThrough.Instructions[1].IsLdcI4()
                    && (
                        block.FallThrough.LastInstr.OpCode == OpCodes.Beq
                        || block.FallThrough.LastInstr.OpCode == OpCodes.Beq_S
                    )
                )
                {
                    if (block.FallThrough.FirstInstr.Operand == block.LastInstr.Operand)
                    {
                        if (
                            block.Instructions[block.Instructions.Count - 2].GetLdcI4Value()
                            == block.FallThrough.Instructions[1].GetLdcI4Value()
                        )
                        {
                            modified = true;
                            Instruction whereJumps = (Instruction)(
                                block.FallThrough.LastInstr.Operand
                            );
                            if (block.Instructions.Count == 2)
                            {
                                block.Instructions.Clear();
                                block.ReplaceLastInstrsWithBranch(0, GetBlock(whereJumps));
                            }
                            else
                            {
                                block
                                    .Instructions[block.Instructions.Count - 2]
                                    .Instruction
                                    .OpCode = OpCodes.Nop;
                                block
                                    .Instructions[block.Instructions.Count - 1]
                                    .Instruction
                                    .OpCode = OpCodes.Nop;
                                block.ReplaceLastInstrsWithBranch(0, GetBlock(whereJumps));
                            }
                        }
                    }
                }
            }

            return modified;
        }
    }
}
