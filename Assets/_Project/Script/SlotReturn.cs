using UnityEngine;

namespace Sand
{
    public class SlotReturn : MonoBehaviour
    {
        [SerializeField] private int slotId;
        [SerializeField] private bool isOccupied;

        public int SlotId => slotId;
        public bool IsOccupied => isOccupied;

        public void SetOccupied(bool occupied)
        {
            isOccupied = occupied;
        }
    }
}
