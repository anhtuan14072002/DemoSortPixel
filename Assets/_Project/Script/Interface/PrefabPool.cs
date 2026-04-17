using System.Collections.Generic;
using UnityEngine;

namespace Pixel
{
    public class PrefabPool<T> where T : Component
    {
        private readonly T prefab;
        private readonly Transform parent;
        private readonly Stack<T> pool = new Stack<T>();
        private readonly HashSet<T> pooledItems = new HashSet<T>();

        public PrefabPool(T prefab, Transform parent = null, int prewarmCount = 0)
        {
            this.prefab = prefab;
            this.parent = parent;

            for (int i = 0; i < prewarmCount; i++)
            {
                var item = CreateNew();
                Release(item);
            }
        }

        private T CreateNew()
        {
            T instance = Object.Instantiate(prefab, parent);
            instance.gameObject.SetActive(false);
            return instance;
        }

        public T Get()
        {
            T item = pool.Count > 0 ? pool.Pop() : CreateNew();
            pooledItems.Remove(item);

            if (item is IPoolable poolable)
                poolable.OnSpawned();
            else
                item.gameObject.SetActive(true);

            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
            if (!pooledItems.Add(item)) return;

            if (item is IPoolable poolable)
                poolable.OnDespawned();
            else
                item.gameObject.SetActive(false);

            item.transform.SetParent(parent, false);
            pool.Push(item);
        }
    }
}
