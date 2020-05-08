using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using R2API.Utils;
using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Linq.Expressions;

namespace TILER2 {
    public static class MiscUtil {
        public static void ReflAddEventHandler(this EventInfo evt, object o, Action<object, EventArgs> lam) {
            var pArr = evt.EventHandlerType.GetMethod("Invoke").GetParameters().Select(p=>Expression.Parameter(p.ParameterType)).ToArray();
            var h = Expression.Lambda(evt.EventHandlerType, Expression.Call(Expression.Constant(lam),lam.GetType().GetMethod("Invoke"),pArr[0],pArr[1]),pArr).Compile();
            evt.AddEventHandler(o, h);
        }

        public class ObservableDictionary<K,V> : IDictionary<K,V>, INotifyCollectionChanged {
            private readonly Dictionary<K,V> _dict;
            private readonly ICollection<KeyValuePair<K,V>> _dictAsColl;
            
            public ObservableDictionary() {
                _dict = new Dictionary<K,V>();
                _dictAsColl = _dict;
            }

            public ICollection<K> Keys => _dict.Keys;
            public ICollection<V> Values => _dict.Values;
            public int Count => _dictAsColl.Count;
            public bool IsReadOnly => false;
            public IEnumerator<KeyValuePair<K,V>> GetEnumerator() => _dict.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _dict.GetEnumerator();
            public bool Contains(KeyValuePair<K,V> p) => _dict.Contains(p);
            public bool TryGetValue(K k, out V v) => _dict.TryGetValue(k, out v);
            public bool ContainsKey(K k) => _dict.ContainsKey(k);
            public void CopyTo(KeyValuePair<K,V>[] pArr, int i) => ((IDictionary<K,V>)_dict).CopyTo(pArr, i);

            public void Add(K k, V v) {
                _dict.Add(k, v);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new KeyValuePair<K, V>(k, v)));
            }
            public void Add(KeyValuePair<K,V> p) {
                _dictAsColl.Add(p);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, p));
            }
            public bool Remove(K k) {
                if(!_dict.ContainsKey(k)) return false;
                var remV = _dict[k];
                _dict.Remove(k);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, new KeyValuePair<K, V>(k,remV)));
                return true;
            }
            public bool Remove(KeyValuePair<K,V> p) {
                var retv = _dictAsColl.Remove(p);
                if(retv) CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, p));
                return retv;
            }

            public void Clear() {
                _dict.Clear();
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

            public V this[K k] {
                get {return _dict[k];}
                set {
                    var oldv = new KeyValuePair<K,V>(k,_dict[k]);
                    _dict[k] = value;
                    CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, new KeyValuePair<K,V>(k,value), oldv));
                }
            }

            public event NotifyCollectionChangedEventHandler CollectionChanged;
        }

        //Collection of unique class instances which all inherit the same type
        public class FilingDictionary<T> : IEnumerable<T> {
            private readonly Dictionary<Type, T> _dict = new Dictionary<Type, T>();

            public int Count => _dict.Count;

            public void Add(T inst) {
                _dict.Add(inst.GetType(), inst);
            }

            public void Add<subT>(subT inst) where subT : T {
                _dict.Add(typeof(subT), inst);
            }

            public void Set<subT>(subT inst) where subT : T {
                _dict[typeof(subT)] = inst;
            }

            public subT Get<subT>() where subT : T {
                return (subT)_dict[typeof(subT)];
            }

            public void Remove(T inst) {
                _dict.Remove(inst.GetType());
            }

            public void RemoveWhere(Func<T, bool> predicate) {
                foreach (var key in _dict.Values.Where(predicate).ToList()) {
                    _dict.Remove(key.GetType());
                }
            }

            public IEnumerator<T> GetEnumerator() {
                return _dict.Values.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return GetEnumerator();
            }
        }

        public static string Pct(float tgt, uint prec = 0, float mult = 100f) {
            return (tgt*mult).ToString("N" + prec) + "%";
        }
        public static string NPlur(float tgt, uint prec = 0) {
            if(prec == 0)
                return (tgt == 1 || tgt == -1) ? "" : "s";
            else
                return (Math.Abs(Math.Abs(tgt)-1) < Math.Pow(10,-prec)) ? "" : "s";
        }
        public static float GetDifficultyCoeffIncreaseAfter(float time, int stages) {
			DifficultyDef difficultyDef = DifficultyCatalog.GetDifficultyDef(Run.instance.selectedDifficulty);
			float num2 = Mathf.Floor((Run.instance.GetRunStopwatch() + time) * 0.0166666675f);
			float num4 = 0.7f + (float)Run.instance.participatingPlayerCount * 0.3f;
			float num7 = 0.046f * difficultyDef.scalingValue * Mathf.Pow((float)Run.instance.participatingPlayerCount, 0.2f);
			float num9 = Mathf.Pow(1.15f, (float)Run.instance.stageClearCount + (float)stages);
			return (num4 + num7 * num2) * num9 - Run.instance.difficultyCoefficient;
        }
        public static List<CharacterMaster> AliveList(bool playersOnly = false) {
            if(playersOnly) return PlayerCharacterMasterController.instances.Where(x=>x.isConnected && x.master && !x.master.IsDeadAndOutOfLivesServer()).Select(x=>x.master).ToList();
            else return CharacterMaster.readOnlyInstancesList.Where(x=>!x.IsDeadAndOutOfLivesServer()).ToList();
        }
        public static void SpawnItemFromBody(CharacterBody src, int tier) {
            List<PickupIndex> spawnList;
            switch(tier) {
                case 1:
                    spawnList = Run.instance.availableTier2DropList;
                    break;
                case 2:
                    spawnList = Run.instance.availableTier3DropList;
                    break;
                case 3:
                    spawnList = Run.instance.availableLunarDropList;
                    break;
                case 4:
                    spawnList = Run.instance.availableNormalEquipmentDropList;
                    break;
                case 5:
                    spawnList = Run.instance.availableLunarEquipmentDropList;
                    break;
                case 0:
                    spawnList = Run.instance.availableTier1DropList;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("tier", tier, "spawnItemFromBody: Item tier must be between 0 and 5 inclusive");
            }
            PickupDropletController.CreatePickupDroplet(spawnList[Run.instance.spawnRng.RangeInt(0,spawnList.Count)], src.transform.position, new Vector3(UnityEngine.Random.Range(-5.0f, 5.0f), 20f, UnityEngine.Random.Range(-5.0f, 5.0f)));
        }

        public static bool RemoveOccupiedNode(this DirectorCore self, RoR2.Navigation.NodeGraph nodeGraph, RoR2.Navigation.NodeGraph.NodeIndex nodeIndex) {
            var ocnf = self.GetType().GetFieldCached("occupiedNodes");
            Array ocn = (Array)ocnf.GetValue(self);
            if(ocn.Length == 0) {
                Debug.LogWarning("TILER2: RemoveOccupiedNode has no nodes to remove");
                return false;
            }
            Array ocnNew = (Array)Activator.CreateInstance(TILER2Plugin.nodeRefTypeArr, ocn.Length - 1);
            IEnumerable ocne = ocn as IEnumerable;
            int i = 0;
            foreach(object o in ocne) {
                var scanInd = o.GetFieldValue<RoR2.Navigation.NodeGraph.NodeIndex>("nodeIndex");
                var scanGraph = o.GetFieldValue<RoR2.Navigation.NodeGraph>("nodeGraph");
                if(object.Equals(scanGraph, nodeGraph) && scanInd.Equals(nodeIndex))
                    continue;
                else if(i == ocn.Length - 1) {
                    Debug.LogWarning("TILER2: RemoveOccupiedNode was passed an already-removed or otherwise nonexistent node");
                    return false;
                }
                ocnNew.SetValue(o, i);
                i++;
            }
            ocnf.SetValue(self, ocnNew);
            return true;
        }
    }
}
