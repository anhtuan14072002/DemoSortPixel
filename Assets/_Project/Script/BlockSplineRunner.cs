﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Sand;
using TMPro;
using PrimeTween;

public class BlockSplineRunner : MonoBehaviour
{
    [Header("Spline")]
    [SerializeField] private SplineContainer splineContainer;

    [Header("Map Ref")]
    [SerializeField] private Transform mapTransform;

    [Header("Return Slots")]
    [SerializeField] private SlotReturn[] slotReturns;

    [Header("Color Match")]
    [SerializeField] private float colorTolerance = 0.05f;

    [Header("Raycast Into Board")]
    [SerializeField] private float castDistance = 8f;
    [SerializeField] private float originBackOffset = 0.15f;
    [SerializeField] private LayerMask cellLayerMask = ~0;
    [SerializeField] private bool drawDebugCast = true;

    [Header("Shoot Prefab")]
    [SerializeField] private GameObject hitPrefab;
    [SerializeField] private Transform projectileSpawnPoint;
    [SerializeField] private float projectileSpeed = 12f;

    [Header("Shoot Control")]
    [SerializeField] private float shootCooldown = 0.03f;
    
    [Header("Run Limit")]
    [SerializeField] private int maxConcurrentRunningBlocks = 5;

    [Header("Ammo UI")]
    [SerializeField] private TMP_Text remainingShotsText;
    [SerializeField] private float outOfShotsDestroyDuration = 0.15f;

    [Header("Return Move")]
    [SerializeField] private float returnJumpDuration = 0.35f;
    [SerializeField] private float returnJumpHeight = 0.6f;

    private SplineAnimate splineAnimate;
    private Collider cachedCollider;
    private MeshRenderer cachedRenderer;
    private MaterialPropertyBlock _mpb;
    private SlotReturn currentSlotReturn;

    private bool isRunning = false;
    private bool isReturningToSlot = false;

    // Cell đã bị xóa xong
    private readonly HashSet<int> deletedInstanceIds = new HashSet<int>();

    // Cell đang bị projectile bay tới, chưa được bắn lại
    private readonly HashSet<int> pendingInstanceIds = new HashSet<int>();

    private int lastHitCellId = -1;
    private int lastShotCellId = -1;
    private float nextAllowedShootTime = 0f;

    private static Camera cam;
    private static int runningBlockCount = 0;
    private int remainingShots;
    private bool shouldDestroyWhenProjectilesFinish;
    private bool isDestroyingOutOfShots;

    public bool IsRunning => isRunning;

    private void Awake()
    {
        splineAnimate = GetComponent<SplineAnimate>();
        cachedCollider = GetComponent<Collider>();
        cachedRenderer = GetComponent<MeshRenderer>();
        if (remainingShotsText == null)
        {
            remainingShotsText = GetComponentInChildren<TMP_Text>(true);
        }

        if (splineAnimate != null)
        {
            splineAnimate.PlayOnAwake = false;
        }

        CacheMainCamera();
        TryResolveSplineContainer();
        TryResolveMapTransform();
        TryResolveSlotReturns();
        UpdateRemainingShotsText();
    }

    private void OnDisable()
    {
        StopRunningState();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            HandleClick();
        }

        if (isRunning)
        {
            if (mapTransform == null)
            {
                TryResolveMapTransform();
            }

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
                if (isRunning)
                {
                    Debug.Log($"BlockSplineRunner: {name} is already running. Click ignored.");
                    return;
                }

                int occupiedSlotCount = GetOccupiedSlotCountForLimit();
                int activeBlockCount = runningBlockCount + occupiedSlotCount;
                if (activeBlockCount >= maxConcurrentRunningBlocks)
                {
                    Debug.Log(
                        $"BlockSplineRunner: click blocked because active blocks reached limit. " +
                        $"Running={runningBlockCount}, OccupiedSlots={occupiedSlotCount}, Limit={maxConcurrentRunningBlocks}."
                    );
                    return;
                }

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

    public void SetSpline(SplineContainer spline)
    {
        splineContainer = spline;
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
        {
            cam = Camera.main;
        }
    }

    private void TryResolveSplineContainer()
    {
        if (splineContainer != null)
            return;

        splineContainer = FindAnyObjectByType<SplineContainer>();
    }

    private void TryResolveMapTransform()
    {
        if (mapTransform != null)
            return;

        SpawnFromTexture mapSource = FindAnyObjectByType<SpawnFromTexture>();
        if (mapSource != null)
        {
            mapTransform = mapSource.transform;
            Debug.Log("Map transform resolved from SpawnFromTexture: " + mapTransform.name);
            return;
        }

        GameObject mapObj = GameObject.Find("Map");
        if (mapObj != null)
        {
            mapTransform = mapObj.transform;
            Debug.Log("Map transform resolved by name: " + mapTransform.name);
            return;
        }

        Debug.LogWarning("BlockSplineRunner: Cannot resolve mapTransform.");
    }

    private void TryResolveSlotReturns()
    {
        if (slotReturns != null && slotReturns.Length > 0)
            return;

        slotReturns = FindObjectsByType<SlotReturn>(FindObjectsSortMode.None);

        if (slotReturns == null || slotReturns.Length == 0)
        {
            Debug.LogWarning("BlockSplineRunner: Cannot resolve SlotReturn in scene.");
            return;
        }

        System.Array.Sort(slotReturns, CompareSlotReturns);
    }

    private int CompareSlotReturns(SlotReturn a, SlotReturn b)
    {
        if (a == null && b == null)
            return 0;

        if (a == null)
            return 1;

        if (b == null)
            return -1;

        int idCompare = a.SlotId.CompareTo(b.SlotId);
        if (idCompare != 0)
            return idCompare;

        return string.CompareOrdinal(a.name, b.name);
    }

    private bool IsHitThisBlock(Collider hitCollider)
    {
        if (hitCollider == null)
            return false;

        if (hitCollider.gameObject == gameObject)
            return true;

        if (cachedCollider != null && hitCollider == cachedCollider)
            return true;

        return hitCollider.transform.IsChildOf(transform);
    }

    private void DetectAndShootNearestCellByRaycast()
    {
        if (mapTransform == null)
            return;

        Vector3 castDirection = GetInwardDirection();
        if (castDirection == Vector3.zero)
            return;

        Vector3 origin = transform.position - castDirection * originBackOffset;

        if (drawDebugCast)
        {
            Debug.DrawRay(origin, castDirection * castDistance, Color.green);
        }

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            castDirection,
            castDistance,
            cellLayerMask
        );

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

        if (currentCellId == lastHitCellId)
        {
            return;
        }

        lastHitCellId = currentCellId;

        if (Time.time < nextAllowedShootTime)
        {
            return;
        }

        TryShootCell(nearestCell);
    }

    private ColorCell GetNearestValidCellFromRay(RaycastHit[] hits)
    {
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
                continue;

            ColorCell cell = hitCollider.GetComponent<ColorCell>();
            if (cell == null)
            {
                cell = hitCollider.GetComponentInParent<ColorCell>();
            }

            if (cell == null)
                continue;

            int id = cell.gameObject.GetInstanceID();

            if (deletedInstanceIds.Contains(id))
                continue;

            if (pendingInstanceIds.Contains(id))
                continue;

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
            if (localPos.x > 0f)
                return -mapTransform.right;

            return mapTransform.right;
        }

        if (localPos.y > 0f)
            return -mapTransform.up;

        return mapTransform.up;
    }

    private void TryShootCell(ColorCell cell)
    {
        if (cell == null)
            return;

        if (remainingShots <= 0)
            return;

        int id = cell.gameObject.GetInstanceID();

        if (deletedInstanceIds.Contains(id))
            return;

        if (pendingInstanceIds.Contains(id))
            return;

        if (id == lastShotCellId)
            return;

        Color myColor = GetMyColor();
        Color cellColor = cell.CellColor;

        if (!IsSameColor(myColor, cellColor))
            return;

        if (hitPrefab == null)
        {
            Debug.LogWarning("BlockSplineRunner: hitPrefab is null.");
            return;
        }

        Vector3 spawnPos = projectileSpawnPoint != null
            ? projectileSpawnPoint.position
            : transform.position;

        GameObject projectileObj = Instantiate(
            hitPrefab,
            spawnPos,
            Quaternion.identity
        );

        ColorProjectile projectile = projectileObj.GetComponent<ColorProjectile>();
        if (projectile == null)
        {
            projectile = projectileObj.AddComponent<ColorProjectile>();
        }

        projectile.Initialize(
            cell,
            myColor,
            colorTolerance,
            projectileSpeed,
            this
        );

        pendingInstanceIds.Add(id);
        lastShotCellId = id;
        nextAllowedShootTime = Time.time + shootCooldown;
        remainingShots--;
        if (remainingShots <= 0)
        {
            shouldDestroyWhenProjectilesFinish = true;
        }
        UpdateRemainingShotsText();
    }

    public void NotifyProjectileFinished(ColorCell cell, bool deleteCell)
    {
        if (cell == null)
            return;

        int id = cell.gameObject.GetInstanceID();

        pendingInstanceIds.Remove(id);

        if (deleteCell)
        {
            if (!deletedInstanceIds.Contains(id))
            {
                deletedInstanceIds.Add(id);
            }

            if (cell != null && cell.gameObject != null)
            {
                cell.PlayDestroyAnimation();
            }
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
            StopRunningState();
            MoveToSlotReturn();
        }
    }

    private void StartRunningState()
    {
        if (isRunning)
            return;

        isRunning = true;
        runningBlockCount++;
    }

    private void StopRunningState()
    {
        if (!isRunning)
            return;

        isRunning = false;
        runningBlockCount = Mathf.Max(0, runningBlockCount - 1);
    }

    private int GetOccupiedSlotCountForLimit()
    {
        TryResolveSlotReturns();

        if (slotReturns == null || slotReturns.Length == 0)
            return 0;

        int occupiedCount = 0;
        for (int i = 0; i < slotReturns.Length; i++)
        {
            if (slotReturns[i] == null || !slotReturns[i].IsOccupied)
                continue;

            if (currentSlotReturn != null && slotReturns[i] == currentSlotReturn)
                continue;

            occupiedCount++;
        }

        return occupiedCount;
    }

    private void MoveToSlotReturn()
    {
        TryResolveSlotReturns();

        if (slotReturns == null || slotReturns.Length == 0)
            return;

        for (int i = 0; i < slotReturns.Length; i++)
        {
            if (slotReturns[i] == null)
                continue;

            if (slotReturns[i].IsOccupied)
                continue;

            slotReturns[i].SetOccupied(true);
            currentSlotReturn = slotReturns[i];
            AnimateMoveToSlotReturn(slotReturns[i]);
            return;
        }

        Debug.LogWarning($"BlockSplineRunner: {name} cannot find an available SlotReturn.");
    }

    private void ReleaseCurrentSlot()
    {
        if (currentSlotReturn == null)
            return;

        currentSlotReturn.SetOccupied(false);
        currentSlotReturn = null;
    }

    private void AnimateMoveToSlotReturn(SlotReturn slotReturn)
    {
        if (slotReturn == null)
            return;

        if (isReturningToSlot)
            return;

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
                Debug.Log($"BlockSplineRunner: {target.name} moved to SlotReturn {slotReturn.name} (Id: {slotReturn.SlotId}).");
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
        if (!shouldDestroyWhenProjectilesFinish)
            return;

        if (pendingInstanceIds.Count > 0)
            return;

        if (isDestroyingOutOfShots)
            return;

        isDestroyingOutOfShots = true;
        StopRunningState();
        ReleaseCurrentSlot();
        Tween.Scale(
            transform,
            Vector3.zero,
            Mathf.Max(0.01f, outOfShotsDestroyDuration),
            Ease.OutQuad
        ).OnComplete(this, target => Destroy(target.gameObject));
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
            if (c != default)
                return c;

            return cachedRenderer.material.GetColor("_Color");
        }

        return Color.white;
    }

    private void UpdateRemainingShotsText()
    {
        if (remainingShotsText == null)
            return;

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
