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
using Microsoft.NodejsTools.Parsing;

namespace Microsoft.NodejsTools.Analysis.Values {
    /// <summary>
    /// Represents a JavaScript object (constructed via a literal or
    /// as the result of a new someFunction call).
    /// </summary>
    internal class ObjectValue : ExpandoValue {
        private readonly FunctionValue _creator;

        public ObjectValue(ProjectEntry projectEntry, FunctionValue creator = null)
            : base(projectEntry) {
            _creator = creator;
        }

        public override Dictionary<string, IAnalysisSet> GetAllMembers() {
            var res = base.GetAllMembers();
            if (_creator != null) {
                PropertyDescriptor prototype;
                if (_creator.InstanceAttributes.TryGetValue("prototype", out prototype) &&
                    prototype.Values != null) {
                    foreach (var value in prototype.Values.TypesNoCopy) {
                        foreach (var kvp in value.GetAllMembers()) {
                            MergeTypes(res, kvp.Key, kvp.Value);
                        }
                    }
                }
            }
            return res;
        }

        public override IAnalysisSet GetMember(Node node, AnalysisUnit unit, string name) {
            var res = base.GetMember(node, unit, name);

            if (_creator != null) {
                var prototype = _creator.GetMember(node, unit, "prototype");
                if (Push()) {
                    try {
                        res = res.Union(prototype.GetMember(node, unit, name));
                    } finally {
                        Pop();
                    }
                }
            }

            return res;
        }

        public override BuiltinTypeId TypeId {
            get {
                return BuiltinTypeId.Object;
            }
        }

        public override JsMemberType MemberType {
            get {
                return JsMemberType.Object;
            }
        }

        public virtual string ObjectDescription {
            get {
                return "object";
            }
        }

        public override string Description {
            get {
                StringBuilder res = new StringBuilder();
                res.Append(ObjectDescription);
                res.Append(' ');

                if (Descriptors != null) {
                    var names = Descriptors
                        .Where(VariableIsDefined)
                        .Select(x => x.Key).ToArray();

                    if (names.Length > 0) {
                        res.AppendLine();
                        res.Append("Contains: ");
                        int lineLength = "Contains: ".Length;
                        Array.Sort(names);
                        for (int i = 0; i < names.Length; i++) {
                            res.Append(names[i]);
                            lineLength += names[i].Length;
                            if (i != names.Length - 1) {
                                res.Append(", ");
                                lineLength += 3;
                            }
                            if (lineLength > 160) {
                                lineLength = 0;
                                res.AppendLine();
                            }
                        }
                    }
                }
                res.AppendLine();
                return res.ToString();
            }
        }

        private static bool VariableIsDefined(KeyValuePair<string, PropertyDescriptor> desc) {
            return (desc.Value.Values != null && desc.Value.Values.VariableStillExists) ||
                   desc.Value.Get != null;
        }

        internal override bool UnionEquals(AnalysisValue ns, int strength) {
            if (strength >= MergeStrength.ToObject) {
                if (ns.TypeId == BuiltinTypeId.Null) {
                    // II + BII(None) => do not merge
                    return false;
                }

                // II + II => BII(object)
                // II + BII => BII(object)
#if FALSE
                var obj = ProjectState.ClassInfos[BuiltinTypeId.Object];
                return ns is InstanceInfo || 
                    //(ns is BuiltinInstanceInfo && ns.TypeId != BuiltinTypeId.Type && ns.TypeId != BuiltinTypeId.Function) ||
                    ns == obj.Instance;
#endif
#if FALSE
            } else if (strength >= MergeStrength.ToBaseClass) {
                var ii = ns as InstanceInfo;
                if (ii != null) {
                    return ii.ClassInfo.UnionEquals(ClassInfo, strength);
                }
                var bii = ns as BuiltinInstanceInfo;
                if (bii != null) {
                    return bii.ClassInfo.UnionEquals(ClassInfo, strength);
                }
#endif
            }

            return base.UnionEquals(ns, strength);
        }

        internal override int UnionHashCode(int strength) {
            if (strength >= MergeStrength.ToObject) {
                //return ProjectState.ClassInfos[BuiltinTypeId.Object].Instance.UnionHashCode(strength);
#if FALSE
            } else if (strength >= MergeStrength.ToBaseClass) {
                return ClassInfo.UnionHashCode(strength);
#endif
            }

            return base.UnionHashCode(strength);
        }

        internal override AnalysisValue UnionMergeTypes(AnalysisValue ns, int strength) {
            if (strength >= MergeStrength.ToObject) {
                // II + II => BII(object)
                // II + BII => BII(object)
                //return ProjectState.ClassInfos[BuiltinTypeId.Object].Instance;
#if FALSE
            } else if (strength >= MergeStrength.ToBaseClass) {
                var ii = ns as InstanceInfo;
                if (ii != null) {
                    return ii.ClassInfo.UnionMergeTypes(ClassInfo, strength).GetInstanceType().Single();
                }
                var bii = ns as BuiltinInstanceInfo;
                if (bii != null) {
                    return bii.ClassInfo.UnionMergeTypes(ClassInfo, strength).GetInstanceType().Single();
                }
#endif
            }

            return base.UnionMergeTypes(ns, strength);
        }
    }
}
