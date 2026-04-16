using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;

public class ColorCell : MonoBehaviour
{
    [SerializeField] private Color cellColor;
    [SerializeField] private float destroyScaleDuration = 0.12f;

    public Color CellColor => cellColor;
    public bool IsDestroying { get; private set; }

    public void SetColor(Color color)
    {
        cellColor = color;
    }

    public void PlayDestroyAnimation()
    {
        if (IsDestroying)
            return;

        IsDestroying = true;
        SetCollidersEnabled(false);
        ShrinkAndDestroy().Forget();
        
    }

    private async UniTask ShrinkAndDestroy()
    {
        Tween.LocalScale(transform, Vector3.one * 1.4f, 0.15f, Ease.Linear).OnComplete(() =>
        {
            Tween.LocalScale(transform, Vector3.zero, 0.35f, Ease.Linear);
        });
        await UniTask.Delay(TimeSpan.FromSeconds(0.5f));
        Destroy(gameObject);
    }

    private void SetCollidersEnabled(bool enabled)
    {
        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = enabled;
        }
    }
}
