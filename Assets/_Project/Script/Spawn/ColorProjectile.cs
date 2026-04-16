using UnityEngine;

public class ColorProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 12f;
    [SerializeField] private float hitDistance = 0.05f;

    private ColorCell targetCell;
    private Color projectileColor;
    private float colorTolerance = 0.05f;
    private BlockSplineRunner owner;

    private MeshRenderer cachedRenderer;
    private MaterialPropertyBlock _mpb;

    public void Initialize(
        ColorCell target,
        Color color,
        float tolerance,
        float moveSpeed,
        BlockSplineRunner runner
    )
    {
        targetCell = target;
        projectileColor = color;
        colorTolerance = tolerance;
        speed = moveSpeed;
        owner = runner;

        cachedRenderer = GetComponent<MeshRenderer>();
        ApplyColor(projectileColor);

        if (targetCell != null)
        {
            transform.forward = (targetCell.transform.position - transform.position).normalized;
        }

        Debug.Log("ColorProjectile initialized: " + (targetCell != null ? targetCell.name : "null target"));
    }

    private void Update()
    {
        if (targetCell == null)
        {
            NotifyOwner(false);
            Destroy(gameObject);
            return;
        }

        Vector3 targetPos = targetCell.transform.position;

        transform.forward = (targetPos - transform.position).normalized;

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPos,
            speed * Time.deltaTime
        );

        float sqrDist = (transform.position - targetPos).sqrMagnitude;
        if (sqrDist <= hitDistance * hitDistance)
        {
            bool sameColor = IsSameColor(projectileColor, targetCell.CellColor);

            NotifyOwner(sameColor);
            Destroy(gameObject);
        }
    }

    private void NotifyOwner(bool deleteCell)
    {
        if (owner != null && targetCell != null)
        {
            owner.NotifyProjectileFinished(targetCell, deleteCell);
        }
    }

    private void ApplyColor(Color color)
    {
        if (cachedRenderer == null)
            return;

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        cachedRenderer.GetPropertyBlock(_mpb);

        if (cachedRenderer.sharedMaterial != null && cachedRenderer.sharedMaterial.HasProperty("_BaseColor"))
        {
            _mpb.SetColor("_BaseColor", color);
            cachedRenderer.SetPropertyBlock(_mpb);
            return;
        }

        if (cachedRenderer.sharedMaterial != null && cachedRenderer.sharedMaterial.HasProperty("_Color"))
        {
            _mpb.SetColor("_Color", color);
            cachedRenderer.SetPropertyBlock(_mpb);
        }
    }

    private bool IsSameColor(Color a, Color b)
    {
        return Vector3.Distance(
            new Vector3(a.r, a.g, a.b),
            new Vector3(b.r, b.g, b.b)
        ) <= colorTolerance;
    }
}