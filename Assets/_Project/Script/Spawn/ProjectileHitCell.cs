using UnityEngine;

public class ProjectileHitCell : MonoBehaviour
{
    [SerializeField] private float speed = 8f;
    [SerializeField] private float lifeTime = 3f;

    private Color targetColor;
    private bool hasInit;

    public void Init(Color color)
    {
        targetColor = color;
        hasInit = true;
        Destroy(gameObject, lifeTime);
    }

    private void Update()
    {
        transform.position += transform.forward * (speed * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!hasInit)
            return;

        ColorCell cell = other.GetComponent<ColorCell>();
        if (cell == null)
        {
            cell = other.GetComponentInParent<ColorCell>();
        }

        if (cell == null)
            return;

        if (IsSameColor(targetColor, cell.CellColor))
        {
            cell.PlayDestroyAnimation();
            Destroy(gameObject);
        }
    }

    private bool IsSameColor(Color a, Color b)
    {
        return Vector3.Distance(
            new Vector3(a.r, a.g, a.b),
            new Vector3(b.r, b.g, b.b)
        ) <= 0.05f;
    }
}
