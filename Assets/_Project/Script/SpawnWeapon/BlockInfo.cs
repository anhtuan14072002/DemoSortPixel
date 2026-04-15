using System;
using UnityEngine;
using UnityEngine.Splines;

namespace Sand
{
    public class BlockInfo : MonoBehaviour
    {
        [SerializeField] private MeshRenderer _meshRenderer;
        [SerializeField] private SplineAnimate _splineAnimate;

        public Color32 BlockColor => _group != null ? _group.RepresentativeColor : default;
        public bool IsAnimating => _isAnimating;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _propertyBlock;
        private ColorGroup _group;
        private bool _isAnimating;

        private void Awake()
        {
            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponent<MeshRenderer>();
            }

            if (_splineAnimate == null)
            {
                _splineAnimate = GetComponent<SplineAnimate>();
            }

            _propertyBlock = new MaterialPropertyBlock();

            if (_splineAnimate != null)
            {
                _splineAnimate.PlayOnAwake = false;
            }
        }

        public void Configure(ColorGroup group)
        {
            _group = group;
            SetColor(group.RepresentativeColor);
        }

        public void SetColor(Color color)
        {
            if (_meshRenderer == null || _propertyBlock == null) return;

            _meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(ColorProperty, color);
            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        public void SetSplineContainer(SplineContainer container)
        {
            if (_splineAnimate == null) return;
            _splineAnimate.Container = container;
        }

        public void PlaySpline()
        {
            if (_isAnimating) return;
            if (_splineAnimate == null) return;
            if (_splineAnimate.Container == null) return;

            _isAnimating = true;
            _splineAnimate.Restart(false);
            _splineAnimate.Play();

            StartCoroutine(WaitUntilSplineDone());
        }

        public Vector3 GetWorldPosition()
        {
            return transform.position;
        }

        private System.Collections.IEnumerator WaitUntilSplineDone()
        {
            while (_splineAnimate != null && _splineAnimate.IsPlaying)
            {
                yield return null;
            }

            _isAnimating = false;
        }
    }
}