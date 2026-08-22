/*
    Copyright (C) 2021 CodeStrikers.org
    This file is part of NETReactorSlayer.
    NETReactorSlayer is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    NETReactorSlayer is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
    You should have received a copy of the GNU General Public License
    along with NETReactorSlayer.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using de4dot.blocks.cflow;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Mono.CompilerServices.SymbolWriter;
using NETReactorSlayer.Core.Abstractions;
using NETReactorSlayer.Core.Helper;

namespace NETReactorSlayer.Core.Stages
{
    internal class ControlFlowDeobfuscator : IStage
    {
        public void Run(IContext context)
        {
            Context = context;
            if (int_fields.Count == 0)
                Initialize();
            long count = 0;
            long CountInlined = 0;
            foreach (
                var method in Context
                    .Module.GetTypes()
                    .SelectMany(type =>
                        (
                            from x in type.Methods
                            where x.HasBody && x.Body.HasInstructions
                            select x
                        ).ToArray()
                    )
            )
            {
                SimpleDeobfuscator.Deobfuscate(method);
                count += Arithmetic(method);
                SimpleDeobfuscator.DeobfuscateBlocks(method);
            }

            foreach (
                var method in Context
                    .Module.GetTypes()
                    .SelectMany(type =>
                        (
                            from x in type.Methods
                            where x.HasBody && x.Body.HasInstructions
                            select x
                        ).ToArray()
                    )
            )
            {
                CountInlined += InlineBoolean(method);
            }

            foreach (
                var method in Context
                    .Module.GetTypes()
                    .SelectMany(type =>
                        (
                            from x in type.Methods
                            where x.HasBody && x.Body.HasInstructions
                            select x
                        ).ToArray()
                    )
            )
            {
                SimpleDeobfuscator.Deobfuscate(method);
                SimpleDeobfuscator.DeobfuscateBlocks(method);
            }

            if (count > 0)
                Context.Logger.Info(count + " Equations resolved.");
            else
                Context.Logger.Warn("Couldn't find any equation to resolve.");

            if (CountInlined > 0)
                Context.Logger.Info(CountInlined + "  ints inlined.");
        }

        private void Initialize()
        {
            FindFieldsStatically();
            if (int_fields.Count < 1)
                FindFieldsDynamically();
        }

        private void FindFieldsStatically()
        {
            TypeDef typeDef = null;
            foreach (
                var type in Context
                    .Module.GetTypes()
                    .Where(x =>
                        x.IsSealed
                        && x.HasFields
                        && x.Fields.Count(f =>
                            f.FieldType.FullName == "System.Int32" && f.IsAssembly && !f.HasConstant
                        ) >= 100
                    )
            )
            {
                int_fields.Clear();
                byte_fields.Clear();
                sbyte_fields.Clear();
                char_fields.Clear();
                string_fields.Clear();
                float_fields.Clear();
                double_fields.Clear();
                short_fields.Clear();
                ushort_fields.Clear();
                uint_fields.Clear();
                long_fields.Clear();
                ulong_fields.Clear();
                bool_fields.Clear();

                foreach (
                    var method in type.Methods.Where(x =>
                        x.IsStatic && x.IsAssembly && x.HasBody && x.Body.HasInstructions
                    )
                )
                {
                    SimpleDeobfuscator.Deobfuscate(method);
                    for (var i = 0; i < method.Body.Instructions.Count; i++)
                        if (
                            (
                                method.Body.Instructions[i].IsLdcI4()
                                && (
                                    i + 1 < method.Body.Instructions.Count
                                        ? method.Body.Instructions[i + 1]
                                        : null
                                )?.OpCode == OpCodes.Stsfld
                            )
                            || (
                                method.Body.Instructions[i].IsLdcI4()
                                && (
                                    i + 1 < method.Body.Instructions.Count
                                        ? method.Body.Instructions[i + 1]
                                        : null
                                )?.OpCode == OpCodes.Stfld
                                && (
                                    i - 1 < method.Body.Instructions.Count
                                        ? method.Body.Instructions[i - 1]
                                        : null
                                )?.OpCode == OpCodes.Ldsfld
                            )
                        )
                        {
                            var key = (IField)
                                (
                                    i + 1 < method.Body.Instructions.Count
                                        ? method.Body.Instructions[i + 1]
                                        : null
                                )?.Operand;
                            var value = method.Body.Instructions[i].GetLdcI4Value();
                            if (key != null && !int_fields.ContainsKey(key))
                                int_fields.Add(key, value);
                            else if (key != null)
                                int_fields[key] = value;
                        }

                    if (int_fields.Count != 0)
                        typeDef = type;
                    goto Continue;
                }
            }

            Continue:
            if (typeDef != null)
                Cleaner.AddTypeToBeRemoved(typeDef);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBox(IntPtr hWnd, String text, String caption, uint type);

        private void FindFieldsDynamically()
        {
            int_fields.Clear();
            byte_fields.Clear();
            sbyte_fields.Clear();
            char_fields.Clear();
            string_fields.Clear();
            float_fields.Clear();
            double_fields.Clear();
            short_fields.Clear();
            ushort_fields.Clear();
            uint_fields.Clear();
            long_fields.Clear();
            ulong_fields.Clear();
            bool_fields.Clear();

            TypeDef typeDef = null;
            TypeDef typeDef2 = null;
            foreach (
                var type in Context
                    .Module.GetTypes()
                    .Where(x =>
                        x.IsSealed
                        && x.HasFields
                        && x.Fields.Count(f =>
                            (f.FieldType.FullName == "System.Int32")
                            && f.IsAssembly
                            && !f.HasConstant
                        ) >= 100
                    )
            )
            {
                if (
                    (Context.Info.NativeStub && Context.Info.NecroBit)
                    || !Context.Info.UsesReflection
                )
                {
                    Context.Logger.Warn("Couldn't resolve arithmetic fields.");
                    return;
                }

                bool FirstTime = true;

                if (
                    type
                        .Fields.Where(x =>
                            x.FieldType.FullName == "System.Int32"
                            || x.FieldType.FullName == "System.UInt32"
                            || x.FieldType.FullName == "System.Int16"
                            || x.FieldType.FullName == "System.UInt16"
                            || x.FieldType.FullName == "System.Int64"
                            || x.FieldType.FullName == "System.UInt64"
                            || x.FieldType.FullName == "System.Boolean"
                            || x.FieldType.FullName == "System.Byte"
                            || x.FieldType.FullName == "System.SByte"
                            || x.FieldType.FullName == "System.Char"
                            || x.FieldType.FullName == "System.String"
                            || x.FieldType.FullName == "System.Single"
                            || x.FieldType.FullName == "System.Double"
                        )
                        .All(x => x.IsStatic)
                )
                    foreach (
                        var field in type.Fields.Where(x =>
                            x.FieldType.FullName == "System.Int32"
                            || x.FieldType.FullName == "System.UInt32"
                            || x.FieldType.FullName == "System.Int64"
                            || x.FieldType.FullName == "System.UInt64"
                            || x.FieldType.FullName == "System.Boolean"
                            || x.FieldType.FullName == "System.Int16"
                            || x.FieldType.FullName == "System.UInt16"
                            || x.FieldType.FullName == "System.Byte"
                            || x.FieldType.FullName == "System.SByte"
                            || x.FieldType.FullName == "System.Char"
                            || x.FieldType.FullName == "System.String"
                            || x.FieldType.FullName == "System.Single"
                            || x.FieldType.FullName == "System.Double"
                        )
                    )
                        try
                        {
                            var obj = Context
                                .Assembly.ManifestModule.ResolveField((int)field.MDToken.Raw)
                                .GetValue(null);

                            if (obj == null)
                                continue;

                            if (obj is int)
                            {
                                int value = (int)obj;
                                if (!int_fields.ContainsKey(field))
                                    int_fields.Add(field, value);
                                else
                                    int_fields[field] = value;
                            }
                            else if (obj is uint)
                            {
                                uint uvalue = (uint)obj;
                                if (!uint_fields.ContainsKey(field))
                                    uint_fields.Add(field, uvalue);
                                else
                                    uint_fields[field] = uvalue;
                            }
                            if (obj is short)
                            {
                                short shortvalue = (short)obj;
                                if (!short_fields.ContainsKey(field))
                                    short_fields.Add(field, shortvalue);
                                else
                                    short_fields[field] = shortvalue;
                            }
                            else if (obj is ushort)
                            {
                                ushort ushortvalue = (ushort)obj;
                                if (!ushort_fields.ContainsKey(field))
                                    ushort_fields.Add(field, ushortvalue);
                                else
                                    ushort_fields[field] = ushortvalue;
                            }
                            else if (obj is ulong)
                            {
                                ulong ulvalue = (ulong)obj;
                                if (!ulong_fields.ContainsKey(field))
                                    ulong_fields.Add(field, ulvalue);
                                else
                                    ulong_fields[field] = ulvalue;
                            }
                            else if (obj is long)
                            {
                                long lvalue = (long)obj;
                                if (!long_fields.ContainsKey(field))
                                    long_fields.Add(field, lvalue);
                                else
                                    long_fields[field] = lvalue;
                            }
                            else if (obj is byte)
                            {
                                byte bvalue = (byte)obj;
                                if (!byte_fields.ContainsKey(field))
                                    byte_fields.Add(field, bvalue);
                                else
                                    byte_fields[field] = bvalue;
                            }
                            else if (obj is sbyte)
                            {
                                sbyte svalue = (sbyte)obj;
                                if (!sbyte_fields.ContainsKey(field))
                                    sbyte_fields.Add(field, svalue);
                                else
                                    sbyte_fields[field] = svalue;
                            }
                            else if (obj is bool)
                            {
                                bool bvalue = (bool)obj;
                                if (!bool_fields.ContainsKey(field))
                                    bool_fields.Add(field, bvalue);
                                else
                                    bool_fields[field] = bvalue;
                            }
                            else if (obj is char)
                            {
                                char charvalue = (char)obj;
                                if (!char_fields.ContainsKey(field))
                                    char_fields.Add(field, charvalue);
                                else
                                    char_fields[field] = charvalue;
                            }
                            else if (obj is string)
                            {
                                string strvalue = (string)obj;
                                if (!string_fields.ContainsKey(field))
                                    string_fields.Add(field, strvalue);
                                else
                                    string_fields[field] = strvalue;
                            }
                            else if (obj is float)
                            {
                                float fvalue = (float)obj;
                                if (!float_fields.ContainsKey(field))
                                    float_fields.Add(field, fvalue);
                                else
                                    float_fields[field] = fvalue;
                            }
                            else if (obj is double)
                            {
                                double dvalue = (double)obj;
                                if (!double_fields.ContainsKey(field))
                                    double_fields.Add(field, dvalue);
                                else
                                    double_fields[field] = dvalue;
                            }
                        }
                        catch (Exception exc)
                        {
                            if (FirstTime)
                            {
                                MessageBox(
                                    new IntPtr(0),
                                    exc.ToString(),
                                    "Exception on Arithmetic fields",
                                    0
                                );
                                FirstTime = false;
                            }
                        }
                else if (
                    type
                        .Fields.Where(x => x.FieldType.FullName == "System.Int32")
                        .All(x => !x.IsStatic)
                )
                    foreach (
                        var instances in type.Fields.Where(x =>
                            x.FieldType.ToTypeDefOrRef().Equals(type)
                        )
                    )
                        try
                        {
                            var instance = Context
                                .Assembly.ManifestModule.ResolveField((int)instances.MDToken.Raw)
                                .GetValue(null);
                            if (instance == null)
                                continue;

                            var runtimeFields = instance.GetType().GetRuntimeFields().ToList();
                            if (runtimeFields.Count(x => x.FieldType == typeof(int)) < 100)
                                continue;

                            foreach (
                                var runtimeField in runtimeFields.Where(x =>
                                    x.FieldType == typeof(int)
                                )
                            )
                            {
                                var field = type.Fields.FirstOrDefault(x =>
                                    x.MDToken.ToInt32().Equals(runtimeField.MetadataToken)
                                );
                                if (field == null)
                                    continue;

                                object obj = runtimeField.GetValue(instance);
                                if (obj == null)
                                    continue;
                                int value = 0;
                                if (obj is int)
                                {
                                    value = (int)obj;
                                    if (!int_fields.ContainsKey(field))
                                        int_fields.Add(field, value);
                                    else
                                        int_fields[field] = value;
                                }
                                // For non-static fields is only int
                            }

                            break;
                        }
                        catch (Exception exc)
                        {
                            if (FirstTime)
                            {
                                MessageBox(
                                    new IntPtr(0),
                                    exc.ToString(),
                                    "Exception on Arithmetic fields",
                                    0
                                );
                                FirstTime = false;
                            }
                        }

                if (int_fields.Count < 100)
                    continue;
                typeDef = type;

                break;
            }

            if (int_fields.All(x => x.Value == 0))
            {
                Context.Logger.Warn("A field has 0 value!");
                int_fields.Clear();
                return;
            }

            if (typeDef != null)
                Cleaner.AddTypeToBeRemoved(typeDef);
        }

        public static bool IsLdc(Instruction ins)
        {
            switch (ins.OpCode.Code)
            {
                case Code.Ldc_I4_M1:
                case Code.Ldc_I4_0:
                case Code.Ldc_I4_1:
                case Code.Ldc_I4_2:
                case Code.Ldc_I4_3:
                case Code.Ldc_I4_4:
                case Code.Ldc_I4_5:
                case Code.Ldc_I4_6:
                case Code.Ldc_I4_7:
                case Code.Ldc_I4_8:
                case Code.Ldc_I4_S:
                case Code.Ldc_I4:
                    return true;

                default:
                    return false;
            }
        }

        public static long InlineBoolean(MethodDef method)
        {
            long count = 0;

            for (var i = 0; i < method.Body.Instructions.Count; i++)
                try
                {
                    if (
                        method.Body.Instructions[i].OpCode != OpCodes.Call
                        || method.Body.Instructions[i].Operand is not MethodDef
                    )
                        continue;

                    MethodDef mdef = method.Body.Instructions[i].Operand as MethodDef;
                    if (!mdef.IsStatic)
                        continue;

                    if (!mdef.HasBody || !mdef.Body.HasInstructions)
                        continue;

                    if (mdef.Body.Instructions.Count == 4)
                    {
                        if (
                            mdef.Body.Instructions[0].OpCode == OpCodes.Ldnull
                            && mdef.Body.Instructions[1].OpCode == OpCodes.Ldnull
                            && mdef.Body.Instructions[3].OpCode == OpCodes.Ret
                        )
                        {
                            if (mdef.Body.Instructions[2].OpCode == OpCodes.Ceq)
                            {
                                method.Body.Instructions[i].OpCode = OpCodes.Ldc_I4_1;
                                method.Body.Instructions[i].Operand = null;
                            }
                        }
                    }

                    if (
                        mdef.Body.Instructions.Count == 2
                        && mdef.Body.Instructions[0].OpCode == OpCodes.Ldnull
                        && mdef.Body.Instructions[1].OpCode == OpCodes.Ret
                    )
                    {
                        method.Body.Instructions[i].OpCode = OpCodes.Ldc_I4_0;
                        method.Body.Instructions[i].Operand = null;
                        count++;
                    }

                    if (mdef.Body.Instructions.Count != 2)
                        continue;

                    if (!IsLdc(mdef.Body.Instructions[0]))
                        continue;

                    if (mdef.Body.Instructions[1].OpCode != OpCodes.Ret)
                        continue;

                    Instruction ldci = Instruction.CreateLdcI4(
                        mdef.Body.Instructions[0].GetLdcI4Value()
                    );
                    method.Body.Instructions[i].OpCode = ldci.OpCode;
                    method.Body.Instructions[i].Operand = ldci.Operand;
                    ldci = null;
                    count++;
                }
                catch { }

            return count;
        }

        public long Arithmetic(MethodDef method)
        {
            long count = 0;
            for (var i = 0; i < method.Body.Instructions.Count; i++)
                try
                {
                    if (
                        (
                            method.Body.Instructions[i].OpCode != OpCodes.Ldsfld
                            && method.Body.Instructions[i].OpCode != OpCodes.Ldfld
                        ) || method.Body.Instructions[i].Operand is not IField
                    )
                        continue;

                    if (
                        int_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var value
                        )
                        && method.DeclaringType != int_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        Instruction ldci = Instruction.CreateLdcI4(value);
                        method.Body.Instructions[i].OpCode = ldci.OpCode;
                        method.Body.Instructions[i].Operand = ldci.Operand;
                        ldci = null;
                        count++;
                    }

                    if (
                        bool_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var boolvalue
                        )
                        && method.DeclaringType != bool_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        if (boolvalue == false)
                        {
                            method.Body.Instructions[i].OpCode = OpCodes.Ldc_I4_0;
                            method.Body.Instructions[i].Operand = null;
                        }
                        else
                        {
                            method.Body.Instructions[i].OpCode = OpCodes.Ldc_I4_1;
                            method.Body.Instructions[i].Operand = null;
                        }

                        count++;
                    }
                    if (
                        uint_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var uivalue
                        )
                        && method.DeclaringType != uint_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        Instruction ldci = Instruction.CreateLdcI4((int)uivalue);
                        method.Body.Instructions[i].OpCode = ldci.OpCode;
                        method.Body.Instructions[i].Operand = ldci.Operand;
                        ldci = null;
                        count++;
                    }

                    if (
                        long_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var lvalue
                        )
                        && method.DeclaringType != long_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;
                        method.Body.Instructions[i].OpCode = OpCodes.Ldc_I8;
                        method.Body.Instructions[i].Operand = (long)lvalue;
                        count++;
                    }

                    if (
                        ulong_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var ulvalue
                        )
                        && method.DeclaringType != ulong_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;
                        method.Body.Instructions[i].OpCode = OpCodes.Ldc_I8;
                        method.Body.Instructions[i].Operand = (long)ulvalue;
                        count++;
                    }

                    if (
                        short_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var svalue
                        )
                        && method.DeclaringType != short_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        Instruction ldci = Instruction.CreateLdcI4((int)svalue);
                        method.Body.Instructions[i].OpCode = ldci.OpCode;
                        method.Body.Instructions[i].Operand = ldci.Operand;
                        ldci = null;
                        count++;
                    }

                    if (
                        ushort_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var usvalue
                        )
                        && method.DeclaringType != ushort_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        Instruction ldci = Instruction.CreateLdcI4((int)usvalue);
                        method.Body.Instructions[i].OpCode = ldci.OpCode;
                        method.Body.Instructions[i].Operand = ldci.Operand;
                        ldci = null;
                        count++;
                    }

                    if (
                        byte_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var bvalue
                        )
                        && method.DeclaringType != byte_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        Instruction ldci = Instruction.CreateLdcI4((int)bvalue);
                        method.Body.Instructions[i].OpCode = ldci.OpCode;
                        method.Body.Instructions[i].Operand = ldci.Operand;
                        ldci = null;
                        count++;
                    }

                    if (
                        sbyte_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var sbvalue
                        )
                        && method.DeclaringType != sbyte_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        Instruction ldci = Instruction.CreateLdcI4((int)sbvalue);
                        method.Body.Instructions[i].OpCode = ldci.OpCode;
                        method.Body.Instructions[i].Operand = ldci.Operand;
                        ldci = null;
                        count++;
                    }

                    if (
                        char_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var cvalue
                        )
                        && method.DeclaringType != char_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        Instruction ldci = Instruction.CreateLdcI4((int)cvalue);
                        method.Body.Instructions[i].OpCode = ldci.OpCode;
                        method.Body.Instructions[i].Operand = ldci.Operand;
                        ldci = null;
                        count++;
                    }

                    if (
                        string_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var stringvalue
                        )
                        && method.DeclaringType != string_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        method.Body.Instructions[i].OpCode = OpCodes.Ldstr;
                        method.Body.Instructions[i].Operand = (string)stringvalue;
                        count++;
                    }

                    if (
                        float_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var fvalue
                        )
                        && method.DeclaringType != float_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        method.Body.Instructions[i].OpCode = OpCodes.Ldc_R4;
                        method.Body.Instructions[i].Operand = (float)fvalue;
                        count++;
                    }

                    if (
                        double_fields.TryGetValue(
                            (IField)method.Body.Instructions[i].Operand,
                            out var dvalue
                        )
                        && method.DeclaringType != double_fields.First().Key.DeclaringType
                    )
                    {
                        if (
                            method.Body.Instructions[i].OpCode == OpCodes.Ldfld
                            && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldsfld
                        )
                            method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;

                        method.Body.Instructions[i].OpCode = OpCodes.Ldc_R8;
                        method.Body.Instructions[i].Operand = (double)dvalue;
                        count++;
                    }
                }
                catch { }

            return count;
        }

        private IContext Context { get; set; }
        private readonly Dictionary<IField, int> int_fields = new();
        private readonly Dictionary<IField, float> float_fields = new();
        private readonly Dictionary<IField, double> double_fields = new();
        private readonly Dictionary<IField, byte> byte_fields = new();
        private readonly Dictionary<IField, sbyte> sbyte_fields = new();
        private readonly Dictionary<IField, string> string_fields = new();
        private readonly Dictionary<IField, char> char_fields = new();
        private readonly Dictionary<IField, short> short_fields = new();
        private readonly Dictionary<IField, ushort> ushort_fields = new();
        private readonly Dictionary<IField, uint> uint_fields = new();
        private readonly Dictionary<IField, long> long_fields = new();
        private readonly Dictionary<IField, ulong> ulong_fields = new();
        private readonly Dictionary<IField, bool> bool_fields = new();
    }
}
