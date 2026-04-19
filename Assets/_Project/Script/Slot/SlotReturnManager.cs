using TMPro;
using UnityEngine;

namespace Pixel
{
    public class SlotReturnManager : MonoBehaviour
    {
        [SerializeField] private TextMeshPro _textCountSlot;
        [SerializeField] private int maxRunningBlocks = 5;

        private void OnEnable()
        {
            BlockSplineRunner.OnRunningCountChanged += UpdateMovingBlockText;
            UpdateMovingBlockText(BlockSplineRunner.RunningCount);
        }

        private void OnDisable()
        {
            BlockSplineRunner.OnRunningCountChanged -= UpdateMovingBlockText;
        }

        private void UpdateMovingBlockText(int movingBlockCount)
        {
            if (_textCountSlot == null) return;
            _textCountSlot.text = movingBlockCount + "/" + maxRunningBlocks;
            Debug.Log("Moving Block Count: " + movingBlockCount);
        }
    }
}