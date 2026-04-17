using TMPro;
using UnityEngine;

namespace Pixel
{
    namespace Sand
    {
        public class SlotReturnManager : MonoBehaviour
        {
            [SerializeField] private TextMeshPro _textCountSlot;

            private BlockSplineRunner[] blockSplineRunners;

            private void Awake()
            {
                TryResolveBlockSplineRunners();
                UpdateMovingBlockText();
            }

            private void Update()
            {
                if (blockSplineRunners == null || blockSplineRunners.Length == 0)
                    TryResolveBlockSplineRunners();
                UpdateMovingBlockText();
            }

            private void TryResolveBlockSplineRunners()
            {
                blockSplineRunners = FindObjectsByType<BlockSplineRunner>(FindObjectsSortMode.None);
            }

            private void UpdateMovingBlockText()
            {
                if (_textCountSlot == null) return;
                int movingBlockCount = 0;

                if (blockSplineRunners != null)
                {
                    for (int i = 0; i < blockSplineRunners.Length; i++)
                    {
                        if (blockSplineRunners[i] == null) continue;
                        if (blockSplineRunners[i].IsRunning)
                            movingBlockCount++;
                    }
                }

                _textCountSlot.text = movingBlockCount + "/" + 5;
            }
        }
    }
}