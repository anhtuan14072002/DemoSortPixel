using UnityEngine;

namespace Pixel
{
    public abstract class PooledObject : MonoBehaviour, IPoolable
    {
        public void OnSpawned()
        {
            gameObject.SetActive(true);
        }

        public void OnDespawned()
        {
            gameObject.SetActive(false);
        }
    }
}