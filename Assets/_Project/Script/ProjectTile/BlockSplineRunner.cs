﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using TMPro;
using PrimeTween;

namespace Pixel
{
    public class BlockSplineRunner : MonoBehaviour, IPoolable
    {
        [SerializeField] private Transform projectileSpawnPoint;
        [SerializeField] private LayerMask cellLayerMask = ~0;
        [SerializeField] private GameObject hitPrefab;
        [SerializeField] private TMP_Text remainingShotsText;

        [SerializeField] private float colorTolerance = 0.05f;
        [SerializeField] private float castDistance = 8f;
        [SerializeField] private float originBackOffset = 0.15f;
        [SerializeField] private float projectileSpeed = 12f;
        [SerializeField] private float shootCooldown = 0.03f;
        [SerializeField] private float outOfShotsDestroyDuration = 0.15f;
        [SerializeField] private float returnJumpDuration = 0.35f;
        [SerializeField] private float returnJumpHeight = 0.6f;
        [SerializeField] private int maxConcurrentRunningBlocks = 5;
        [SerializeField] private bool drawDebugCast = true;

        private SplineContainer splineContainer;
        private SlotReturn[] slotReturns;
        private Transform mapTransform;
        private SplineAnimate splineAnimate;
        private Collider cachedCollider;
        private MeshRenderer cachedRenderer;
        private MaterialPropertyBlock _mpb;
        private SlotReturn currentSlotReturn;

        private bool isRunning = false;
        private bool isReturningToSlot = false;

        // Cell đã bị xóa xong
        private readonly HashSet<int> deletedInstanceIds = new();
        // Cell đang bị projectile bay tới, chưa được bắn lại
        private readonly HashSet<int> pendingInstanceIds = new();

        private int lastHitCellId = -1;
        private int lastShotCellId = -1;
        private float nextAllowedShootTime = 0f;

        private static Camera cam;
        private static int runningBlockCount = 0;
        private static readonly HashSet<BlockSplineRunner> activeBlocks = new();
        private int remainingShots;
        private bool shouldDestroyWhenProjectilesFinish;
        private bool isDestroyingOutOfShots;
        private PrefabPool<ColorProjectile> projectilePool;
        private PrefabPool<BlockSplineRunner> blockPool;
        private ColorProjectile projectilePrefabComponent;
        private Vector3 initialLocalScale;

        public bool IsRunning => isRunning;

        public void SetPool(PrefabPool<BlockSplineRunner> prefabPool)
        {
            blockPool = prefabPool;
        }

        public void OnSpawned()
        {
            gameObject.SetActive(true);
            transform.localScale = initialLocalScale;
            isDestroyingOutOfShots = false;
            isReturningToSlot = false;
            shouldDestroyWhenProjectilesFinish = false;
            ReleaseCurrentSlot();
        }

        public void OnDespawned()
        {
            StopRunningState();
            ReleaseCurrentSlot();
            deletedInstanceIds.Clear();
            pendingInstanceIds.Clear();
            lastHitCellId = -1;
            lastShotCellId = -1;
            isDestroyingOutOfShots = false;
            isReturningToSlot = false;
            shouldDestroyWhenProjectilesFinish = false;
            gameObject.SetActive(false);
        }

        private void Awake()
        {
            splineAnimate = GetComponent<SplineAnimate>();
            cachedCollider = GetComponent<Collider>();
            cachedRenderer = GetComponent<MeshRenderer>();
            initialLocalScale = transform.localScale;
            
            
            projectilePrefabComponent = hitPrefab != null ? hitPrefab.GetComponent<ColorProjectile>() : null;
            if (projectilePrefabComponent != null)
                projectilePool = new PrefabPool<ColorProjectile>(projectilePrefabComponent, null);

            if (splineAnimate != null)
                splineAnimate.PlayOnAwake = false;

            CacheMainCamera();
            TryResolveSplineContainer();
            TryResolveMapTransform();
            TryResolveSlotReturns();
            UpdateRemainingShotsText();
        }

        private void OnDisable()
        {
            StopRunningState();
            activeBlocks.Remove(this);
        }

        private void OnEnable()
        {
            activeBlocks.Add(this);
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0)) HandleClick();

            if (isRunning)
            {
                if (mapTransform == null) TryResolveMapTransform();
                DetectAndShootNearestCellByRaycast();
                CheckSplineFinished();
            }
        }

        private void HandleClick()
        {
            CacheMainCamera();
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (IsHitThisBlock(hit.collider))
                {
                    if (isRunning) return;
                    if (runningBlockCount >= maxConcurrentRunningBlocks) return;
                    Run();
                }
            }
        }

        public void Run()
        {
            if (splineAnimate == null) return;

            TryResolveSplineContainer();
            TryResolveMapTransform();
            TryResolveSlotReturns();

            if (splineContainer == null) return;

            ReleaseCurrentSlot();

            deletedInstanceIds.Clear();
            pendingInstanceIds.Clear();
            lastHitCellId = -1;
            lastShotCellId = -1;
            nextAllowedShootTime = 0f;

            splineAnimate.Container = splineContainer;
            splineAnimate.Restart(true);
            splineAnimate.Play();

            StartRunningState();
        }

        public void InitializeShotLimit(int shotCount)
        {
            remainingShots = Mathf.Max(0, shotCount);
            shouldDestroyWhenProjectilesFinish = remainingShots == 0;
            UpdateRemainingShotsText();
        }

        private void CacheMainCamera()
        {
            if (cam == null || !cam.isActiveAndEnabled)
                cam = Camera.main;
        }

        private void TryResolveSplineContainer()
        {
            if (splineContainer != null) return;
            splineContainer = FindAnyObjectByType<SplineContainer>();
        }

        private void TryResolveMapTransform()
        {
            if (mapTransform != null) return;
            RenderMap mapSource = FindAnyObjectByType<RenderMap>();
            if (mapSource != null)
            {
                mapTransform = mapSource.transform;
                return;
            }

            GameObject mapObj = GameObject.Find("Map");

            if (mapObj != null)
            {
                mapTransform = mapObj.transform;
                return;
            }
        }

        private void TryResolveSlotReturns()
        {
            if (slotReturns != null && slotReturns.Length > 0) return;
            slotReturns = FindObjectsByType<SlotReturn>(FindObjectsSortMode.None);
            if (slotReturns == null || slotReturns.Length == 0) return;
            System.Array.Sort(slotReturns, CompareSlotReturns);
        }

        private int CompareSlotReturns(SlotReturn a, SlotReturn b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int idCompare = a.SlotId.CompareTo(b.SlotId);
            if (idCompare != 0) return idCompare;
            return string.CompareOrdinal(a.name, b.name);
        }

        private bool IsHitThisBlock(Collider hitCollider)
        {
            if (hitCollider == null) return false;
            if (hitCollider.gameObject == gameObject) return true;
            if (cachedCollider != null && hitCollider == cachedCollider) return true;
            return hitCollider.transform.IsChildOf(transform);
        }

        private void DetectAndShootNearestCellByRaycast()
        {
            if (mapTransform == null) return;
            Vector3 castDirection = GetInwardDirection();
            if (castDirection == Vector3.zero) return;
            Vector3 origin = transform.position - castDirection * originBackOffset;
            if (drawDebugCast)
            {
                Debug.DrawRay(origin, castDirection * castDistance, Color.green);
            }

            RaycastHit[] hits = Physics.RaycastAll(origin, castDirection, castDistance, cellLayerMask);
            if (hits == null || hits.Length == 0)
            {
                lastHitCellId = -1;
                return;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            ColorCell nearestCell = GetNearestValidCellFromRay(hits);

            if (nearestCell == null)
            {
                lastHitCellId = -1;
                return;
            }

            int currentCellId = nearestCell.gameObject.GetInstanceID();
            if (currentCellId == lastHitCellId) return;
            lastHitCellId = currentCellId;
            if (Time.time < nextAllowedShootTime) return;
            TryShootCell(nearestCell);
        }

        private ColorCell GetNearestValidCellFromRay(RaycastHit[] hits)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null) continue;

                ColorCell cell = hitCollider.GetComponent<ColorCell>();
                if (cell == null)
                    cell = hitCollider.GetComponentInParent<ColorCell>();
                if (cell == null) continue;
                int id = cell.gameObject.GetInstanceID();
                if (deletedInstanceIds.Contains(id)) continue;
                if (pendingInstanceIds.Contains(id)) continue;
                return cell;
            }

            return null;
        }

        private Vector3 GetInwardDirection()
        {
            Vector3 localPos = mapTransform.InverseTransformPoint(transform.position);

            float absX = Mathf.Abs(localPos.x);
            float absY = Mathf.Abs(localPos.y);

            if (absX >= absY)
            {
                if (localPos.x > 0f) return -mapTransform.right;
                return mapTransform.right;
            }

            if (localPos.y > 0f) return -mapTransform.up;
            return mapTransform.up;
        }

        private void TryShootCell(ColorCell cell)
        {
            if (cell == null) return;
            if (remainingShots <= 0) return;
            int id = cell.gameObject.GetInstanceID();
            if (deletedInstanceIds.Contains(id)) return;
            if (pendingInstanceIds.Contains(id)) return;
            if (id == lastShotCellId) return;

            Color myColor = GetMyColor();
            Color cellColor = cell.CellColor;

            if (!IsSameColor(myColor, cellColor)) return;
            if (hitPrefab == null) return;
            if (projectilePool == null)
            {
                projectilePrefabComponent = hitPrefab.GetComponent<ColorProjectile>();
                if (projectilePrefabComponent == null) return;
                projectilePool = new PrefabPool<ColorProjectile>(projectilePrefabComponent, null);
            }

            Vector3 spawnPos = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;

            ColorProjectile projectile = projectilePool.Get();
            projectile.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);

            projectile.Initialize(cell, myColor, colorTolerance, projectileSpeed, this);

            pendingInstanceIds.Add(id);
            lastShotCellId = id;
            nextAllowedShootTime = Time.time + shootCooldown;
            remainingShots--;

            if (remainingShots <= 0)
                shouldDestroyWhenProjectilesFinish = true;

            UpdateRemainingShotsText();
        }

        public void NotifyProjectileFinished(ColorCell cell, bool deleteCell)
        {
            if (cell == null) return;
            int id = cell.gameObject.GetInstanceID();

            pendingInstanceIds.Remove(id);

            if (deleteCell)
            {
                if (!deletedInstanceIds.Contains(id))
                    deletedInstanceIds.Add(id);
                if (cell != null && cell.gameObject != null)
                    cell.PlayDestroyAnimation();
            }

            TryDestroyIfOutOfShots();
        }

        private void CheckSplineFinished()
        {
            if (splineAnimate == null)
            {
                StopRunningState();
                return;
            }

            if (splineAnimate.NormalizedTime >= 1f)
            {
                if (ShouldLoopInsteadOfReturnToSlot())
                {
                    RestartSplineLoop();
                    return;
                }

                StopRunningState();
                MoveToSlotReturn();
            }
        }

        private bool ShouldLoopInsteadOfReturnToSlot()
        {
            return GetActiveBlockCount() <= maxConcurrentRunningBlocks;
        }

        private void RestartSplineLoop()
        {
            if (splineAnimate == null) return;

            ReleaseCurrentSlot();
            splineAnimate.Restart(true);
            splineAnimate.Play();
            Time.timeScale = 2f;
        }

        private int GetActiveBlockCount()
        {
            activeBlocks.RemoveWhere(block => block == null || !block.gameObject.activeInHierarchy);
            return activeBlocks.Count;
        }

        private void StartRunningState()
        {
            if (isRunning) return;

            isRunning = true;
            runningBlockCount++;
        }

        private void StopRunningState()
        {
            if (!isRunning) return;

            isRunning = false;
            runningBlockCount = Mathf.Max(0, runningBlockCount - 1);
        }

        private void MoveToSlotReturn()
        {
            TryResolveSlotReturns();

            if (slotReturns == null || slotReturns.Length == 0) return;

            for (int i = 0; i < slotReturns.Length; i++)
            {
                if (slotReturns[i] == null) continue;
                if (slotReturns[i].IsOccupied) continue;

                slotReturns[i].SetOccupied(true);
                currentSlotReturn = slotReturns[i];
                AnimateMoveToSlotReturn(slotReturns[i]);
                return;
            }
        }

        private void ReleaseCurrentSlot()
        {
            if (currentSlotReturn == null) return;

            currentSlotReturn.SetOccupied(false);
            currentSlotReturn = null;
        }

        private void AnimateMoveToSlotReturn(SlotReturn slotReturn)
        {
            if (slotReturn == null) return;
            if (isReturningToSlot) return;
            isReturningToSlot = true;

            Vector3 targetLocalPosition = GetTargetLocalPosition(slotReturn.transform.position);
            float baseLocalZ = targetLocalPosition.z;
            float duration = Mathf.Max(0.01f, returnJumpDuration);

            Sequence jumpSequence = Sequence.Create()
                .Chain(Tween.LocalPositionZ(transform, baseLocalZ + returnJumpHeight, duration * 0.5f, Ease.OutQuad))
                .Chain(Tween.LocalPositionZ(transform, baseLocalZ, duration * 0.5f, Ease.InQuad));

            Sequence.Create()
                .Group(Tween.LocalPosition(transform, targetLocalPosition, duration, Ease.InOutSine))
                .Group(jumpSequence)
                .OnComplete(this, target =>
                {
                    target.transform.rotation = slotReturn.transform.rotation;
                    target.isReturningToSlot = false;
                });
        }

        private Vector3 GetTargetLocalPosition(Vector3 worldPosition)
        {
            if (transform.parent == null)
                return worldPosition;

            return transform.parent.InverseTransformPoint(worldPosition);
        }

        private void TryDestroyIfOutOfShots()
        {
            if (!shouldDestroyWhenProjectilesFinish) return;
            if (pendingInstanceIds.Count > 0) return;
            if (isDestroyingOutOfShots) return;

            isDestroyingOutOfShots = true;
            StopRunningState();
            ReleaseCurrentSlot();
            Tween.Scale(transform, Vector3.zero, Mathf.Max(0.01f, outOfShotsDestroyDuration), Ease.OutQuad
            ).OnComplete(this, target =>
            {
                if (target.blockPool != null)
                    target.blockPool.Release(target);
                else
                    target.gameObject.SetActive(false);
            });
        }

        public void ReleaseProjectile(ColorProjectile projectile)
        {
            if (projectilePool != null)
                projectilePool.Release(projectile);
            else if (projectile != null)
                projectile.gameObject.SetActive(false);
        }

        public Color GetMyColor()
        {
            if (cachedRenderer == null)
                return Color.white;

            if (_mpb == null)
                _mpb = new MaterialPropertyBlock();

            cachedRenderer.GetPropertyBlock(_mpb);
            if (cachedRenderer.sharedMaterial != null && cachedRenderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                Color c = _mpb.GetColor("_BaseColor");
                if (c != default)
                    return c;
                return cachedRenderer.material.GetColor("_BaseColor");
            }

            if (cachedRenderer.sharedMaterial != null && cachedRenderer.sharedMaterial.HasProperty("_Color"))
            {
                Color c = _mpb.GetColor("_Color");
                if (c != default) return c;

                return cachedRenderer.material.GetColor("_Color");
            }

            return Color.white;
        }

        private void UpdateRemainingShotsText()
        {
            if (remainingShotsText == null) return;
            remainingShotsText.text = remainingShots.ToString();
        }

        private bool IsSameColor(Color a, Color b)
        {
            return Vector3.Distance(
                new Vector3(a.r, a.g, a.b),
                new Vector3(b.r, b.g, b.b)
            ) <= colorTolerance;
        }
    }
}
