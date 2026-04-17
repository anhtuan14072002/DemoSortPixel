using System;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;

namespace Pixel
{
    public class ColorCell : MonoBehaviour, IPoolable
    {
        [SerializeField] private Color cellColor;
        [SerializeField] private float destroyScaleDuration = 0.12f;
        [SerializeField] private Collider _collider;

        public Color CellColor => cellColor;
        public bool IsDestroying { get; private set; }
        private PrefabPool<ColorCell> pool;
        private Vector3 initialLocalScale;

        private void Awake()
        {
            initialLocalScale = transform.localScale;
            if (_collider == null)
                _collider = GetComponent<Collider>();
        }

        public void SetPool(PrefabPool<ColorCell> prefabPool)
        {
            pool = prefabPool;
        }

        public void OnSpawned()
        {
            IsDestroying = false;
            transform.localScale = initialLocalScale;

            if (_collider != null)
                _collider.enabled = true;

            gameObject.SetActive(true);
        }

        public void OnDespawned()
        {
            IsDestroying = false;
            gameObject.SetActive(false);
        }

        //Set color cho cell
        public void SetColor(Color color)
        {
            cellColor = color;
        }

        public void PlayDestroyAnimation()
        {
            if (IsDestroying) return;

            IsDestroying = true;
            if (_collider != null)
                _collider.enabled = false;
            ShrinkAndDestroy().Forget();
        }

        private async UniTask ShrinkAndDestroy()
        {
            Tween.LocalScale(transform, Vector3.one * 1.4f, 0.15f, Ease.Linear).OnComplete(() =>
            {
                Tween.LocalScale(transform, Vector3.zero, 0.35f, Ease.Linear);
            });
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f));
            if (pool != null)
                pool.Release(this);
            else
                gameObject.SetActive(false);
        }
    }
}
