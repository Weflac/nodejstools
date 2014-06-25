﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;

using System.Text;
using Microsoft.NodejsTools.Analysis.Analyzer;
using Microsoft.NodejsTools.Parsing;

namespace Microsoft.NodejsTools.Analysis.Values {
    [Serializable]
    class UserFunctionValue : FunctionValue {
        private readonly FunctionObject _funcObject;
        private readonly FunctionAnalysisUnit _analysisUnit;
        public VariableDef ReturnValue;
        internal Dictionary<CallArgs, CallInfo> _allCalls;
        private OverflowState _overflowed;
        const int MaximumCallCount = 30;

        public UserFunctionValue(FunctionObject node, AnalysisUnit declUnit, EnvironmentRecord declScope, bool isNested = false)
            : base(declUnit.ProjectEntry, true, node.Name ?? node.NameGuess) {
            ReturnValue = new VariableDef();
            _funcObject = node;
            _analysisUnit = new FunctionAnalysisUnit(this, declUnit, declScope, ProjectEntry);

            declUnit.Analyzer.AnalysisValueCreated(typeof(UserFunctionValue));
        }

        public FunctionObject FunctionObject {
            get {
                return _funcObject;
            }
        }

        public override string Documentation {
            get {
#if FALSE
                if (FunctionObject.Body != null) {
                    return FunctionObject.Body.Documentation.TrimDocumentation();
                }
#endif
                return "";
            }
        }

        public override IEnumerable<LocationInfo> Locations {
            get {
                return new[] { 
                    FunctionObject.GlobalParent.ResolveLocation(ProjectEntry, FunctionObject)
                };
            }
        }

        public override AnalysisUnit AnalysisUnit {
            get {
                return _analysisUnit;
            }
        }

        public override string ToString() {
            return String.Format("UserFunction {0} {1}\r\n{2}", 
                FunctionObject.Name,
                FunctionObject.GetStart(ProjectEntry.Tree),
                ProjectEntry.FilePath
            );
        }

        public override string Description {
            get {
                var result = new StringBuilder();
                {
                    result.Append("function ");
                    if (FunctionObject.Name != null) {
                        result.Append(FunctionObject.Name);
                    } else {
                        result.Append("<anonymous>");
                    }
                    result.Append("(");
                    AddParameterString(result);
                    result.Append(")");
                }

                AddReturnTypeString(result);
                AddDocumentationString(result);

                return result.ToString();
            }
        }

        private static void AppendDescription(StringBuilder result, AnalysisValue key) {
            result.Append(key.ShortDescription);
        }

        public override string Name {
            get {
                if (FunctionObject != null) {
                    return FunctionObject.Name ?? FunctionObject.NameGuess;
                }
                return "<unknown function>";
            }
        }

        internal void AddParameterString(StringBuilder result) {
            if (FunctionObject.ParameterDeclarations != null) {
                for (int i = 0; i < FunctionObject.ParameterDeclarations.Count; i++) {
                    if (i != 0) {
                        result.Append(", ");
                    }
                    var p = FunctionObject.ParameterDeclarations[i];

                    var name = MakeParameterName(p);
                    result.Append(name);
                }
            }
        }

        internal void AddReturnTypeString(StringBuilder result) {
            bool first = true;
            var retTypes = ReturnValue.Types;
            if (retTypes.Count == 0) {
                return;
            }
            if (retTypes.Count <= 10) {
                var seenNames = new HashSet<string>();
                foreach (var av in retTypes) {
                    if (av == null) {
                        continue;
                    }

                    if (av.Push()) {
                        try {
                            if (!string.IsNullOrWhiteSpace(av.ShortDescription) && seenNames.Add(av.ShortDescription)) {
                                if (first) {
                                    result.Append(" -> ");
                                    first = false;
                                } else {
                                    result.Append(", ");
                                }
                                AppendDescription(result, av);
                            }
                        } finally {
                            av.Pop();
                        }
                    } else {
                        result.Append("...");
                    }
                }
            }
        }

        internal void AddDocumentationString(StringBuilder result) {
            if (!String.IsNullOrEmpty(Documentation)) {
                result.AppendLine();
                result.Append(Documentation);
            }
        }

        public override string OwnerName {
            get {
                if (FunctionObject.Name == null) {
                    return "";
                }

                return FunctionObject.Name;
            }
        }

        class ArgumentsWalker : AstVisitor {
            public bool UsesArguments;

            public override bool Walk(Lookup node) {
                if (node.Name == "arguments") {
                    UsesArguments = true;
                }
                return base.Walk(node);
            }

            public override bool Walk(FunctionObject node) {
                return false;
            }
        }

        public override IEnumerable<OverloadResult> Overloads {
            get {
                var references = new Dictionary<string[], IEnumerable<AnalysisVariable>[]>(new StringArrayComparer());

                var vars = FunctionObject.ParameterDeclarations.Select(p => {
                    VariableDef param;
                    if (AnalysisUnit.Environment.TryGetVariable(p.Name, out param)) {
                        return param;
                    }
                    return null;
                }).ToArray();

                var parameters = vars
                    .Select(p => string.Join(", ", p.TypesNoCopy.Select(av => av.ShortDescription).Where(s => !String.IsNullOrWhiteSpace(s)).OrderBy(s => s).Distinct()))
                    .ToArray();

                IEnumerable<AnalysisVariable>[] refs = vars.Select(v => VariableTransformer.OtherToVariables.ToVariables(v)).ToArray();

                ParameterResult[] parameterResults = FunctionObject.ParameterDeclarations == null ?
                    new ParameterResult[0] :
                    FunctionObject.ParameterDeclarations.Select((p, i) => 
                        new ParameterResult(
                            MakeParameterName(p), 
                            string.Empty, 
                            parameters[i], 
                            false, 
                            refs[i]
                        )
                    ).ToArray();

                var argsWalker = new ArgumentsWalker();
                FunctionObject.Body.Walk(argsWalker);
                if (argsWalker.UsesArguments) {
                    parameterResults = parameterResults.Concat(
                        new[] { new ParameterResult("...") }
                    ).ToArray();
                }

                yield return new SimpleOverloadResult(
                    parameterResults,
                    FunctionObject.Name ?? "<anonymous function>",
                    Documentation
                );
            }
        }

        public override IAnalysisSet Call(Node node, AnalysisUnit unit, IAnalysisSet @this, IAnalysisSet[] args) {
            if (_overflowed == OverflowState.OverflowedBigTime) {
                return _analysisUnit.ReturnValue.Types;
            }

            var callArgs = new CallArgs(@this, args, _overflowed == OverflowState.OverflowedOnce);

            CallInfo callInfo;

            if (_allCalls == null) {
                _allCalls = new Dictionary<CallArgs, CallInfo>();
            }

            if (!_allCalls.TryGetValue(callArgs, out callInfo)) {
                if (unit.ForEval) {
                    return ReturnValue.Types;
                }

                _allCalls[callArgs] = callInfo = new CallInfo(this, 
                    _analysisUnit.Environment, 
                    ((FunctionAnalysisUnit)_analysisUnit)._declUnit, 
                    callArgs
                );

                if (_allCalls.Count > 10) {
                    // try and compress args using UnionEquality...
                    if (_overflowed == OverflowState.None) {
                        _overflowed = OverflowState.OverflowedOnce;
                        var newAllCalls = new Dictionary<CallArgs, CallInfo>();
                        foreach (var keyValue in _allCalls) {
                            newAllCalls[new CallArgs(@this, keyValue.Key.Args, overflowed: true)] = keyValue.Value;
                        }
                        _allCalls = newAllCalls;
                    }
                }

                if (_allCalls.Count > MaximumCallCount) {
                    _overflowed = OverflowState.OverflowedBigTime;
                    return _analysisUnit.ReturnValue.Types;
                }

                callInfo.ReturnValue.AddDependency(unit);

                callInfo.AnalysisUnit.Enqueue();
                return AnalysisSet.Empty;
            } else {
                callInfo.ReturnValue.AddDependency(unit);
                return callInfo.ReturnValue.Types;
            }
        }


        private class StringArrayComparer : IEqualityComparer<string[]> {
            private IEqualityComparer<string> _comparer;

            public StringArrayComparer() {
                _comparer = StringComparer.Ordinal;
            }

            public StringArrayComparer(IEqualityComparer<string> comparer) {
                _comparer = comparer;
            }

            public bool Equals(string[] x, string[] y) {
                if (x == null || y == null) {
                    return x == null && y == null;
                }

                if (x.Length != y.Length) {
                    return false;
                }

                for (int i = 0; i < x.Length; ++i) {
                    if (!_comparer.Equals(x[i], y[i])) {
                        return false;
                    }
                }
                return true;
            }

            public int GetHashCode(string[] obj) {
                if (obj == null) {
                    return 0;
                }
                int hc = 261563 ^ obj.Length;
                foreach (var p in obj) {
                    hc ^= _comparer.GetHashCode(p);
                }
                return hc;
            }
        }


        internal override bool UnionEquals(AnalysisValue av, int strength) {
            if (strength >= MergeStrength.ToObject) {
                var func = av as UserFunctionValue;
                if (func != null) {
                    return true;
                }
            }

            if (strength >= MergeStrength.ToBaseClass) {
                var func = av as UserFunctionValue;
                if (func != null) {
                    // two literals from the same node, these
                    // literals were created by independent function
                    // analysis, merge them together now.
                    return func.FunctionObject == FunctionObject;
                }
            }
            return base.UnionEquals(av, strength);
        }

        internal override int UnionHashCode(int strength) {
            if (strength >= MergeStrength.ToObject) {
                return ProjectState._functionPrototype.GetHashCode();
            }

            if (strength >= MergeStrength.ToBaseClass) {
                return FunctionObject.GetHashCode();
            }
            return base.UnionHashCode(strength);
        }

        internal override AnalysisValue UnionMergeTypes(AnalysisValue av, int strength) {
            if (strength >= MergeStrength.ToObject) {
                var func = av as UserFunctionValue;
                if (func != null) {
                    return this;
                }
            }

            if (strength >= MergeStrength.ToBaseClass) {
                var func = av as UserFunctionValue;
                if (func != null && func.FunctionObject == FunctionObject) {
                    return this;
                }
            }

            return base.UnionMergeTypes(av, strength);
        }

        /// <summary>
        /// Hashable set of arguments for analyzing the cartesian product of the received arguments.
        /// 
        /// Each time we get called we check and see if we've seen the current argument set.  If we haven't
        /// then we'll schedule the function to be analyzed for those args (and if that results in a new
        /// return type then we'll use the return type to analyze afterwards).
        /// </summary>
        [Serializable]
        internal class CallArgs : IEquatable<CallArgs> {
            public readonly IAnalysisSet This;
            public readonly IAnalysisSet[] Args;
            private int _hashCode;

            public CallArgs(IAnalysisSet @this, IAnalysisSet[] args, bool overflowed) {
                if (!overflowed) {
                    for (int i = 0; i < args.Length; i++) {                        
                        if (args[i].Count >= 10) {
                            overflowed = true;
                            break;
                        }
                    }
                }
                if (overflowed) {
                    for (int i = 0; i < args.Length; i++) {
                        args[i] = args[i].AsUnion(2);
                    }
                }
                This = @this;
                Args = args;
            }

            public override string ToString() {
                StringBuilder res = new StringBuilder();

                if (This != null) {
                    bool appended = false;
                    foreach (var @this in This) {
                        if (appended) {
                            res.Append(", ");
                        }
                        res.Append(@this.ToString());
                        appended = true;
                    }
                    res.Append(" ");
                }

                res.Append("{");
                foreach (var arg in Args) {
                    res.Append("{");
                    bool appended = false;
                    foreach (var argVal in arg) {
                        if (appended) {
                            res.Append(", ");
                        }
                        res.Append(argVal.ToString());
                        res.Append(" ");
                        res.Append(GetComparer().GetHashCode(argVal));
                        appended = true;
                    }
                    res.Append("}");
                }
                res.Append("}");
                return res.ToString();
            }

            public override bool Equals(object obj) {
                CallArgs other = obj as CallArgs;
                if (other != null) {
                    return Equals(other);
                }
                return false;
            }

            #region IEquatable<CallArgs> Members

            public bool Equals(CallArgs other) {
                if (Object.ReferenceEquals(this, other)) {
                    return true;
                }
                
                if (Args.Length != other.Args.Length) {
                    return false;
                }

                if (This != null) {
                    if (other.This != null) {
                        if (This.Count != other.This.Count) {
                            return false;
                        }
                    } else if (This.Count != 0) {
                        return false;
                    }
                } else if (other.This != null && other.This.Count > 0) {
                    return false;
                }

                if (This != null) {
                    foreach (var @this in This) {
                        if (!other.This.Contains(@this)) {
                            return false;
                        }
                    }
                }

                for (int i = 0; i < Args.Length; i++) {
                    if (Args[i].Count != other.Args[i].Count) {
                        return false;
                    }
                }

                for (int i = 0; i < Args.Length; i++) {
                    foreach (var arg in Args[i]) {
                        if (!other.Args[i].Contains(arg)) {
                            return false;
                        }
                    }
                }
                return true;
            }

            #endregion

            public override int GetHashCode() {
                if (_hashCode == 0) {
                    int hc = 6551;
                    if (Args.Length > 0) {
                        IEqualityComparer<AnalysisValue> comparer = GetComparer();
                        for (int i = 0; i < Args.Length; i++) {
                            foreach (var value in Args[i]) {
                                hc ^= comparer.GetHashCode(value);
                            }
                        }
                    }
                    if (This != null) {
                        foreach (var value in This) {
                            hc ^= value.GetHashCode();
                        }
                    }

                    _hashCode = hc;
                }
                return _hashCode;
            }

            private IEqualityComparer<AnalysisValue> GetComparer() {
                var arg0 = Args[0] as HashSet<AnalysisValue>;
                IEqualityComparer<AnalysisValue> comparer;
                if (arg0 != null) {
                    comparer = arg0.Comparer;
                } else {
                    comparer = EqualityComparer<AnalysisValue>.Default;
                }
                return comparer;
            }
        }

        [Serializable]
        internal class CallInfo {
            public readonly VariableDef ReturnValue;
            public readonly CartesianProductFunctionAnalysisUnit AnalysisUnit;

            public CallInfo(UserFunctionValue funcValue, EnvironmentRecord environment, AnalysisUnit outerUnit, CallArgs args) {
                ReturnValue = new VariableDef();
                AnalysisUnit = new CartesianProductFunctionAnalysisUnit(
                    funcValue, 
                    environment, 
                    outerUnit, 
                    args, 
                    ReturnValue
                );
            }
        }

        enum OverflowState {
            None,
            OverflowedOnce,
            OverflowedBigTime
        }
    }
}