﻿using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.NodejsTools.Analysis.Analyzer;
using Microsoft.NodejsTools.Parsing;

namespace Microsoft.NodejsTools.Analysis.Values {
    /// <summary>
    /// Base class for functions and objects.  Supports getting/setting
    /// arbitrary values on the object and indexing into the object using
    /// strings.
    /// </summary>
    class ExpandoValue : AnalysisValue, IReferenceableContainer {
        private readonly ProjectEntry _projectEntry;
        private readonly int _declVersion;
        internal readonly DependentKeyValue _keysAndValues;
        private Dictionary<string, PropertyDescriptor> _descriptors;
        private AnalysisValue _next;
//        private VariableDef _keysVariable, _valuesVariable, _keyValueTupleVariable;

        internal ExpandoValue(ProjectEntry projectEntry) {
            _projectEntry = projectEntry;
            _declVersion = projectEntry.AnalysisVersion;
            _keysAndValues = new DependentKeyValue();
        }

        public ProjectEntry ProjectEntry {
            get {
                return _projectEntry;
            }
        }

        public override IJsProjectEntry DeclaringModule {
            get {
                return _projectEntry;
            }
        }

        public override int DeclaringVersion {
            get {
                return _declVersion;
            }
        }

        internal Dictionary<string, PropertyDescriptor> InstanceAttributes {
            get {
                return _descriptors;
            }
        }

        public override IAnalysisSet GetMember(Node node, AnalysisUnit unit, string name) {
            if (_descriptors == null) {
                _descriptors = new Dictionary<string, PropertyDescriptor>();
            }
            PropertyDescriptor desc;
            if (!_descriptors.TryGetValue(name, out desc)) {
                _descriptors[name] = desc = new PropertyDescriptor();
            }
            if (desc.Values == null) {
                desc.Values = new EphemeralVariableDef();
            }

            desc.Values.AddDependency(unit);
            desc.Values.AddReference(node, unit);

            var res = desc.Values.Types;

            if (desc.Get != null) {
                res = res.Union(desc.Get.TypesNoCopy.Call(node, unit, AnalysisSet.Empty, ExpressionEvaluator.EmptySets));
            }

            if (_next != null && _next.Push()) {
                try {
                    res = res.Union(_next.GetMember(node, unit, name));
                } finally {
                    _next.Pop();
                }
            }
            return res;
        }

        public override void SetMember(Node node, AnalysisUnit unit, string name, IAnalysisSet value) {
            PropertyDescriptor desc;
            if (_descriptors != null && _descriptors.TryGetValue(name, out desc)) {
                if (desc.Set != null) {
                    desc.Set.TypesNoCopy.Call(
                        node,
                        unit,
                        AnalysisSet.Empty,
                        new[] { value }
                    );
                }
            }

            VariableDef varRef = GetValuesDef(name);

            varRef.AddAssignment(node, unit);
            varRef.MakeUnionStrongerIfMoreThan(ProjectState.Limits.InstanceMembers, value);
            varRef.AddTypes(unit, value);
        }

        public override void DeleteMember(Node node, AnalysisUnit unit, string name) {
            VariableDef def = GetValuesDef(name);

            def.AddReference(node, unit);
        }

        private VariableDef GetValuesDef(string name) {
            PropertyDescriptor desc = GetDescriptor(name);

            VariableDef def = desc.Values;
            if (def == null) {
                desc.Values = def = new VariableDef();
            }
            return def;
        }

        public override void SetIndex(Node node, AnalysisUnit unit, IAnalysisSet index, IAnalysisSet value) {
            foreach (var type in index) {
                string strValue;
                if ((strValue = type.GetConstantValueAsString()) != null) {
                    // x = {}; x['abc'] = 42; should be available as x.abc
                    SetMember(node, unit, strValue, value);
                }
            }
            AddTypes(node, unit, index, value);
        }

        public override IAnalysisSet GetIndex(Node node, AnalysisUnit unit, IAnalysisSet index) {
            _keysAndValues.AddDependency(unit);
            var res = _keysAndValues.GetValueType(index);
            foreach (var value in index) {
                string strValue;
                if ((strValue = value.GetConstantValueAsString()) != null) {
                    res = res.Union(GetMember(node, unit, strValue));
                }
            }
            return res;
        }

        internal bool AddTypes(Node node, AnalysisUnit unit, IAnalysisSet key, IAnalysisSet value, bool enqueue = true) {
            if (_keysAndValues.AddTypes(unit, key, value, enqueue)) {
#if FALSE
                if (_keysVariable != null) {
                    _keysVariable.MakeUnionStrongerIfMoreThan(ProjectState.Limits.DictKeyTypes, value);
                    if (_keysVariable.AddTypes(unit, key, enqueue)) {
                        if (_keysIter != null)
                        {
                            _keysIter.UnionType = null;
                        }
                        if (_keysList != null)
                        {
                            _keysList.UnionType = null;
                        }
                    }
                }
#endif
#if FALSE
                if (_valuesVariable != null) {
                    _valuesVariable.MakeUnionStrongerIfMoreThan(ProjectState.Limits.DictValueTypes, value);
                    if (_valuesVariable.AddTypes(unit, value, enqueue)) {
                        if (_valuesIter != null)
                        {
                            _valuesIter.UnionType = null;
                        }
                        if (_valuesList != null)
                        {
                            _valuesList.UnionType = null;
                        }
                    }
                }
#endif
#if FALSE
                if (_keyValueTuple != null)
                {
                    _keyValueTuple.IndexTypes[0].MakeUnionStrongerIfMoreThan(ProjectState.Limits.DictKeyTypes, key);
                    _keyValueTuple.IndexTypes[1].MakeUnionStrongerIfMoreThan(ProjectState.Limits.DictValueTypes, value);
                    _keyValueTuple.IndexTypes[0].AddTypes(unit, key, enqueue);
                    _keyValueTuple.IndexTypes[1].AddTypes(unit, value, enqueue);
                }
#endif
                return true;
            }
            return false;
        }

        public override Dictionary<string, IAnalysisSet> GetAllMembers() {
            if (_descriptors == null || _descriptors.Count == 0) {
                return new Dictionary<string, IAnalysisSet>();
#if FALSE
                return ProjectState.ClassInfos[BuiltinTypeId.Function].GetAllMembers(moduleContext);
#endif
            }

            var res = new Dictionary<string, IAnalysisSet>();
            foreach (var kvp in _descriptors) {
                var key = kvp.Key;
                if (kvp.Value.Values != null) {
                    var types = kvp.Value.Values.TypesNoCopy;
                    kvp.Value.Values.ClearOldValues();
                    if (kvp.Value.Values.VariableStillExists) {
                        MergeTypes(res, key, types);
                    }
                }

                if (kvp.Value.Get != null || kvp.Value.Set != null) {
                    MergeTypes(res, key, AnalysisSet.Empty);
                }
            }

            if (_next != null) {
                MergeDictionaries(res, _next.GetAllMembers());
            }

            return res;
        }

        internal static void MergeDictionaries(Dictionary<string, IAnalysisSet> target, Dictionary<string, IAnalysisSet> source) {
            foreach (var keyValue in source) {
                MergeTypes(target, keyValue.Key, keyValue.Value);
            }
        }

        public JsAnalyzer ProjectState { get { return ProjectEntry.Analyzer; } }

        /// <summary>
        /// Adds a member from the built-in module.
        /// </summary>
        public VariableDef Add(string name, IAnalysisSet value) {
            PropertyDescriptor desc = GetDescriptor(name);

            VariableDef def = desc.Values;
            if (def == null) {
                desc.Values = def = new VariableDef();
            }

            def.AddTypes(ProjectState._builtinEntry, value);
            return def;
        }

        public void Add(string name, VariableDef variable) {
            PropertyDescriptor desc = GetDescriptor(name);

            Debug.Assert(desc.Values == null);
            desc.Values = variable;
        }

        public void Add(AnalysisValue value) {
            Add(value.Name, value.SelfSet);
        }

        public void Add(MemberAddInfo member) {
            if (!member.IsProperty) {
                Add(member.Name, member.Value);
            } else {
                PropertyDescriptor desc = GetDescriptor(member.Name);

                VariableDef def = desc.Get;
                if (def == null) {
                    desc.Get = def = new VariableDef();
                }

                def.AddTypes(ProjectState._builtinEntry, new ReturningFunctionValue(ProjectEntry, member.Name, member.Value));
            }
        }

        public void AddProperty(Node node, AnalysisUnit unit, string name, AnalysisValue value) {
            PropertyDescriptor desc = GetDescriptor(name);

            var get = value.GetMember(node, unit, "get");
            if (get.Count > 0) {
                if (desc.Get == null) {
                    desc.Get = new VariableDef();
                }
                desc.Get.AddTypes(unit, get);
            }

            var set = value.GetMember(node, unit, "set");
            if (set.Count > 0) {
                if (desc.Set == null) {
                    desc.Set = new VariableDef();
                }
                desc.Set.AddTypes(unit, set);
            }
        }

        private PropertyDescriptor GetDescriptor(string name) {
            EnsureDescriptors();

            PropertyDescriptor desc;
            if (!_descriptors.TryGetValue(name, out desc)) {
                _descriptors[name] = desc = new PropertyDescriptor();
            }
            return desc;
        }

        private void EnsureDescriptors() {
            if (_descriptors == null) {
                _descriptors = new Dictionary<string, PropertyDescriptor>();
            }
        }

        protected static void MergeTypes(Dictionary<string, IAnalysisSet> res, string key, IEnumerable<AnalysisValue> types) {
            IAnalysisSet set;
            if (!res.TryGetValue(key, out set)) {
                res[key] = set = AnalysisSet.Create(types);
            } else {
                res[key] = set.Union(types);
            }
        }

        public Dictionary<string, PropertyDescriptor> Descriptors {
            get {
                return _descriptors;
            }
        }

        /// <summary>
        /// Makes this analysis value include all of the source analysis
        /// values and automatically pick up new values if the source
        /// changes.
        /// </summary>
        /// <param name="source"></param>
        public void AddLinkedValue(AnalysisValue source) {
            if (_next == null) {
                _next = source;
            } else if (_next != source) {
                if (_next is LinkedAnalysisList) {
                    ((LinkedAnalysisList)_next).AddLink(source);
                } else {
                    _next = new LinkedAnalysisList(_next, source);
                }
            }
        }

        class LinkedAnalysisList : AnalysisValue {
            private readonly List<AnalysisValue> _values;

            public LinkedAnalysisList(AnalysisValue one, AnalysisValue two) {
                _values = new List<AnalysisValue>() { one, two };
            }

            public override IAnalysisSet GetMember(Node node, AnalysisUnit unit, string name) {
                var res = AnalysisSet.Empty;
                foreach (var value in _values) {
                    res = res.Union(value.GetMember(node, unit, name));
                }
                return res;
            }

            public override Dictionary<string, IAnalysisSet> GetAllMembers() {
                var res = new Dictionary<string, IAnalysisSet>();
                foreach (var value in _values) {
                    ExpandoValue.MergeDictionaries(res, value.GetAllMembers());
                }
                return res;
            }

            internal void AddLink(AnalysisValue source) {
                _values.Add(source);
            }
        }

        #region IReferenceableContainer Members

        public IEnumerable<IReferenceable> GetDefinitions(string name) {
            PropertyDescriptor desc;
            // TODO: access descriptor support
            if (_descriptors != null && _descriptors.TryGetValue(name, out desc)) {
                if (desc.Values != null) {
                    return new IReferenceable[] { desc.Values };
                }
            }
            return new IReferenceable[0];
        }

        #endregion
    }

    /// <summary>
    /// Represents the descriptor state for a given property.
    /// 
    /// We track all of Values, Get, Set and merge them together,
    /// so if a property is changing between the two we'll see
    /// the union.
    /// 
    /// We don't currently track anything like writable/enumerable/configurable.
    /// </summary>
    class PropertyDescriptor {
        public VariableDef Values, Get, Set;
    }
}
