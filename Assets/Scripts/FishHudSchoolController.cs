using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Camera))]
public class FishHudSchoolController : MonoBehaviour
{
    private const string FishPrefabAssetPath = "Assets/assets/fish.prefab";
    private const string SharkPrefabAssetPath = "Assets/assets/Shark.prefab";
    private const float HudTextScale = 1.5f;

    private static class HudLayout
    {
        public static readonly Vector2 PanelPosition = new Vector2(20f, -20f);
        public static readonly Vector2 PanelSize = new Vector2(360f, 274f);
        public static readonly Vector2 HeaderPosition = new Vector2(12f, -10f);
        public static readonly Vector2 HeaderSize = new Vector2(336f, 24f);
        public static readonly Vector2 FooterPosition = new Vector2(12f, -240f);
        public static readonly Vector2 FooterSize = new Vector2(336f, 18f);
        public static readonly Vector2 LabelSize = new Vector2(120f, 24f);
        public static readonly Vector2 SliderOffset = new Vector2(124f, -(LabelSize.y * HudTextScale - 18f) * 0.5f);
        public static readonly Vector2 SliderSize = new Vector2(160f, 18f);
        public static readonly Vector2 ValueTextOffset = new Vector2(292f, 0f);
        public static readonly Vector2 ValueTextSize = new Vector2(48f, 24f);
        public static readonly Vector2 ButtonSize = new Vector2(102f, 28f);
        public const float ContentLeft = 12f;
        public const float FirstRowY = -74f;
        public const float RowSpacing = 44f;
        public const float ButtonRowY = -208f;
        public const float ButtonSpacing = 12f;

        public static Vector2 GetRowPosition(int rowIndex)
        {
            return new Vector2(ContentLeft, FirstRowY - (RowSpacing * rowIndex));
        }

        public static Vector2 GetButtonPosition(int buttonIndex)
        {
            float totalWidth = (ButtonSize.x * 3f) + (ButtonSpacing * 2f);
            float startX = (PanelSize.x - totalWidth) * 0.5f;
            return new Vector2(startX + (buttonIndex * (ButtonSize.x + ButtonSpacing)), ButtonRowY);
        }
    }

    [Header("Scene References")]
    [SerializeField] private Transform tankBoundsSource;
    [SerializeField] private GameObject fishPrefab;
    [SerializeField] private GameObject predatorPrefab;

    [Header("School Controls")]
    [SerializeField, Range(0, 9999)] private int seed = 1;
    [SerializeField, Range(1, 1000)] private int numberOfFish = 600;

    [Header("Spawn Settings")]
    [SerializeField] private Vector3 tankPadding = new Vector3(0.9f, 1.2f, 0.9f);
    [SerializeField] private float schoolRadius = 2f;
    [SerializeField] private float minimumSpawnSpacing = 0.8f;
    [SerializeField, Range(1, 32)] private int spawnPositionAttempts = 10;

    [Header("Performance")]
    [SerializeField] private float spatialGridCellSize = 1.5f;
    [SerializeField, Range(1, 5)] private int staggerGroups = 3;
    private int staggerFrame;

    [Header("Predator")]
    [SerializeField] private bool spawnPredatorOnStart = true;
    [SerializeField, Range(1, 5)] private int numberOfPredators = 1;
    [SerializeField] private float predatorMinimumSpacing = 2f;
    [SerializeField] private Vector3 predatorSpawnOffsetNormalized = new Vector3(0.72f, 0.08f, -0.72f);
    [SerializeField] private Vector3 predatorVisualRotationEuler = new Vector3(0f, 180f, 0f);

    private Camera attachedCamera;
    private Transform fishRoot;
    private Slider seedSlider;
    private Slider fishCountSlider;
    private Slider sharkCountSlider;
    private Text seedValueText;
    private Text fishCountValueText;
    private Text sharkCountValueText;
    private bool schoolMovementActive;
    private bool missingTankWarningShown;
    private bool fallbackVisualWarningShown;
    private bool missingPredatorPrefabWarningShown;

    // Right panel sliders
    private Slider sharkSpeedSlider;
    private Slider sharkTurnSpeedSlider;
    private Slider fishSpeedSlider;
    private Slider fishTurnSpeedSlider;
    private Slider fishBurstSpeedSlider;
    private Slider avoidRadiusSlider;
    private Slider avoidStrengthSlider;
    private Slider maxAvoidForceSlider;
    private Slider lateralFleeSlider;
    private Text sharkSpeedValueText;
    private Text sharkTurnSpeedValueText;
    private Text fishSpeedValueText;
    private Text fishTurnSpeedValueText;
    private Text fishBurstSpeedValueText;
    private Text avoidRadiusValueText;
    private Text avoidStrengthValueText;
    private Text maxAvoidForceValueText;
    private Text lateralFleeValueText;

    private void Awake()
    {
        attachedCamera = GetComponent<Camera>();
        ResolveSceneReferences();
        EnsureEventSystem();
        BuildHud();
    }

    private void OnValidate()
    {
        ResolveSceneReferences();
    }

    private void Start()
    {
        ApplySettings();
    }

    private void Update()
    {
        SyncSettingsFromHud();
        SyncTuningToInstances();

        float dt = Time.deltaTime;

        FishMovement.UpdateCache();

        FishSpatialGrid grid = FishMovement.SpatialGrid;
        if (grid != null)
        {
            grid.UpdateGrid(FishMovement.ActiveFish);
        }

        int currentGroup = staggerFrame % staggerGroups;
        staggerFrame++;

        // Check if any predator exists so we can force recalc for nearby fish
        bool anyPredatorActive = PredatorMovement.ActivePredators.Count > 0;

        for (int i = 0; i < FishMovement.ActiveFish.Count; i++)
        {
            FishMovement fish = FishMovement.ActiveFish[i];
            if (fish != null)
            {
                bool recalc = (i % staggerGroups) == currentGroup;
                if (!recalc && anyPredatorActive)
                {
                    recalc = fish.IsPredatorNearby();
                }
                fish.UpdateMovement(dt, recalc);
            }
        }

        for (int i = 0; i < PredatorMovement.ActivePredators.Count; i++)
        {
            PredatorMovement predator = PredatorMovement.ActivePredators[i];
            if (predator != null)
            {
                predator.UpdateMovement(dt);
            }
        }
    }

    private void ResolveSceneReferences()
    {
        if (tankBoundsSource == null)
        {
            GameObject waterVolume = GameObject.Find("WaterVolume");
            if (waterVolume != null)
            {
                tankBoundsSource = waterVolume.transform;
            }
        }

        ResolveFishPrefabReference();
        ResolvePredatorPrefabReference();
    }

    private void ResolveFishPrefabReference()
    {
        if (fishPrefab != null)
        {
            return;
        }

#if UNITY_EDITOR
        fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishPrefabAssetPath);
#endif
    }

    private void ResolvePredatorPrefabReference()
    {
        if (predatorPrefab != null)
        {
            return;
        }

#if UNITY_EDITOR
        predatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SharkPrefabAssetPath);
#endif
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        InputSystemUIInputModule inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
        inputModule.AssignDefaultActions();
    }

    private void BuildHud()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject canvasObject = new GameObject("FishHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = attachedCamera;
        canvas.planeDistance = 0.5f;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject panelObject = new GameObject("FishHudPanel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = HudLayout.PanelPosition;
        panelRect.sizeDelta = HudLayout.PanelSize;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.05f, 0.11f, 0.18f, 0.82f);

        CreateText(panelObject.transform, font, "Tank Controls", 18, TextAnchor.MiddleLeft, HudLayout.HeaderPosition, HudLayout.HeaderSize);
        CreateText(panelObject.transform, font, "Press Esc to unlock the cursor for UI.", 12, TextAnchor.MiddleLeft, HudLayout.FooterPosition, HudLayout.FooterSize);

        int rowIndex = 0;
        seedSlider = CreateLabeledSlider(panelObject.transform, font, "SEED", rowIndex++, 0f, 9999f, out seedValueText);
        fishCountSlider = CreateLabeledSlider(panelObject.transform, font, "number of fish", rowIndex++, 1f, 1000f, out fishCountValueText);
        sharkCountSlider = CreateLabeledSlider(panelObject.transform, font, "number of sharks", rowIndex++, 1f, 5f, out sharkCountValueText);
        CreateButton(panelObject.transform, font, "Play", HudLayout.GetButtonPosition(0), HudLayout.ButtonSize, OnPlayClicked);
        CreateButton(panelObject.transform, font, "Pause", HudLayout.GetButtonPosition(1), HudLayout.ButtonSize, OnPauseClicked);
        CreateButton(panelObject.transform, font, "Reset", HudLayout.GetButtonPosition(2), HudLayout.ButtonSize, OnResetClicked);

        seedSlider.wholeNumbers = true;
        fishCountSlider.wholeNumbers = true;
        sharkCountSlider.wholeNumbers = true;
        seedSlider.SetValueWithoutNotify(seed);
        fishCountSlider.SetValueWithoutNotify(numberOfFish);
        sharkCountSlider.SetValueWithoutNotify(numberOfPredators);
        UpdateValueLabels();

        seedSlider.onValueChanged.AddListener(OnSeedChanged);
        fishCountSlider.onValueChanged.AddListener(OnFishCountChanged);
        sharkCountSlider.onValueChanged.AddListener(OnSharkCountChanged);

        // Right panel - runtime tuning
        BuildRightPanel(canvasObject.transform, font);
    }

    private void BuildRightPanel(Transform canvasTransform, Font font)
    {
        int totalRows = 11; // 2 headers + 9 sliders
        float rightPanelHeight = -HudLayout.FirstRowY + (HudLayout.RowSpacing * totalRows) + 20f;
        GameObject rightPanelObject = new GameObject("TuningPanel", typeof(RectTransform), typeof(Image));
        rightPanelObject.transform.SetParent(canvasTransform, false);

        RectTransform rightPanelRect = rightPanelObject.GetComponent<RectTransform>();
        rightPanelRect.anchorMin = new Vector2(1f, 1f);
        rightPanelRect.anchorMax = new Vector2(1f, 1f);
        rightPanelRect.pivot = new Vector2(1f, 1f);
        rightPanelRect.anchoredPosition = new Vector2(-20f, -20f);
        rightPanelRect.sizeDelta = new Vector2(HudLayout.PanelSize.x, rightPanelHeight);

        Image rightPanelImage = rightPanelObject.GetComponent<Image>();
        rightPanelImage.color = new Color(0.05f, 0.11f, 0.18f, 0.82f);

        CreateText(rightPanelObject.transform, font, "Tuning", 18, TextAnchor.MiddleLeft, HudLayout.HeaderPosition, HudLayout.HeaderSize);

        int rowIndex = 0;

        // Shark settings
        CreateText(rightPanelObject.transform, font, "— Shark —", 13, TextAnchor.MiddleLeft, HudLayout.GetRowPosition(rowIndex), HudLayout.LabelSize);
        rowIndex++;
        sharkSpeedSlider = CreateLabeledSlider(rightPanelObject.transform, font, "speed", rowIndex++, 0.1f, 10f, out sharkSpeedValueText);
        sharkTurnSpeedSlider = CreateLabeledSlider(rightPanelObject.transform, font, "turn speed", rowIndex++, 0.05f, 3f, out sharkTurnSpeedValueText);

        // Fish settings
        CreateText(rightPanelObject.transform, font, "— Fish —", 13, TextAnchor.MiddleLeft, HudLayout.GetRowPosition(rowIndex), HudLayout.LabelSize);
        rowIndex++;
        fishSpeedSlider = CreateLabeledSlider(rightPanelObject.transform, font, "speed", rowIndex++, 0.1f, 10f, out fishSpeedValueText);
        fishTurnSpeedSlider = CreateLabeledSlider(rightPanelObject.transform, font, "turn speed", rowIndex++, 0.5f, 20f, out fishTurnSpeedValueText);
        fishBurstSpeedSlider = CreateLabeledSlider(rightPanelObject.transform, font, "burst spd", rowIndex++, 0.1f, 15f, out fishBurstSpeedValueText);
        avoidRadiusSlider = CreateLabeledSlider(rightPanelObject.transform, font, "avoid rad", rowIndex++, 0.5f, 15f, out avoidRadiusValueText);
        avoidStrengthSlider = CreateLabeledSlider(rightPanelObject.transform, font, "avoid str", rowIndex++, 1f, 200f, out avoidStrengthValueText);
        maxAvoidForceSlider = CreateLabeledSlider(rightPanelObject.transform, font, "max force", rowIndex++, 1f, 200f, out maxAvoidForceValueText);
        lateralFleeSlider = CreateLabeledSlider(rightPanelObject.transform, font, "lateral flee", rowIndex++, 0f, 1f, out lateralFleeValueText);

        // Set initial values from prefab defaults
        sharkSpeedSlider.SetValueWithoutNotify(3f);
        sharkTurnSpeedSlider.SetValueWithoutNotify(1f);
        fishSpeedSlider.SetValueWithoutNotify(1.5f);
        fishTurnSpeedSlider.SetValueWithoutNotify(6f);
        fishBurstSpeedSlider.SetValueWithoutNotify(2.5f);
        avoidRadiusSlider.SetValueWithoutNotify(3f);
        avoidStrengthSlider.SetValueWithoutNotify(50f);
        maxAvoidForceSlider.SetValueWithoutNotify(40f);
        lateralFleeSlider.SetValueWithoutNotify(0.55f);

        UpdateRightPanelValueLabels();
    }

    private void ApplySettings()
    {
        seed = Mathf.Clamp(seed, 0, 9999);
        numberOfFish = Mathf.Clamp(numberOfFish, 1, 1000);
        numberOfPredators = Mathf.Clamp(numberOfPredators, 1, 5);

        if (seedSlider != null)
        {
            seedSlider.SetValueWithoutNotify(seed);
        }

        if (fishCountSlider != null)
        {
            fishCountSlider.SetValueWithoutNotify(numberOfFish);
        }

        if (sharkCountSlider != null)
        {
            sharkCountSlider.SetValueWithoutNotify(numberOfPredators);
        }

        UpdateValueLabels();
        UnityEngine.Random.InitState(seed);
        RebuildSchool();
        SetSchoolMovementActive(schoolMovementActive);
    }

    private void SyncSettingsFromHud()
    {
        bool settingsChanged = false;

        if (seedSlider != null)
        {
            int sliderSeed = Mathf.Clamp(Mathf.RoundToInt(seedSlider.value), 0, 9999);
            if (seed != sliderSeed)
            {
                seed = sliderSeed;
                settingsChanged = true;
            }
        }

        if (fishCountSlider != null)
        {
            int sliderFishCount = Mathf.Clamp(Mathf.RoundToInt(fishCountSlider.value), 1, 1000);
            if (numberOfFish != sliderFishCount)
            {
                numberOfFish = sliderFishCount;
                settingsChanged = true;
            }
        }

        if (sharkCountSlider != null)
        {
            int sliderSharkCount = Mathf.Clamp(Mathf.RoundToInt(sharkCountSlider.value), 1, 5);
            if (numberOfPredators != sliderSharkCount)
            {
                numberOfPredators = sliderSharkCount;
                settingsChanged = true;
            }
        }

        UpdateValueLabels();

        if (settingsChanged)
        {
            ApplySettings();
        }
    }

    private void OnSeedChanged(float value)
    {
        seed = Mathf.RoundToInt(value);
        ApplySettings();
    }

    private void OnFishCountChanged(float value)
    {
        numberOfFish = Mathf.RoundToInt(value);
        ApplySettings();
    }

    private void OnSharkCountChanged(float value)
    {
        numberOfPredators = Mathf.RoundToInt(value);
        ApplySettings();
    }

    private void OnPlayClicked()
    {
        SetSchoolMovementActive(true);
    }

    private void OnPauseClicked()
    {
        SetSchoolMovementActive(false);
    }

    private void OnResetClicked()
    {
        schoolMovementActive = false;
        ApplySettings();
    }

    private void RebuildSchool()
    {
        ResolveSceneReferences();

        if (!TryGetTankBounds(out Vector3 tankCenter, out Vector3 tankExtents))
        {
            if (!missingTankWarningShown)
            {
                Debug.LogWarning("FishHudSchoolController needs a valid tank bounds source.", this);
                missingTankWarningShown = true;
            }

            return;
        }

        missingTankWarningShown = false;

        if (fishPrefab == null && !fallbackVisualWarningShown)
        {
            Debug.LogWarning("FishHudSchoolController could not resolve the fish prefab reference. Using generated fallback fish visuals instead.", this);
            fallbackVisualWarningShown = true;
        }

        if (spawnPredatorOnStart && predatorPrefab == null && !missingPredatorPrefabWarningShown)
        {
            Debug.LogWarning("FishHudSchoolController could not resolve the Shark predator prefab reference.", this);
            missingPredatorPrefabWarningShown = true;
        }

        if (fishRoot == null)
        {
            fishRoot = new GameObject("FishSchoolRuntime").transform;
            fishRoot.SetParent(tankBoundsSource.parent, false);
        }

        ClearExistingFish();

        FishMovement.SetSpatialGrid(new FishSpatialGrid(tankCenter, tankExtents, spatialGridCellSize));

        System.Random random = new System.Random(seed);

        if (spawnPredatorOnStart)
        {
            System.Random predatorRandom = new System.Random(seed + 7919);
            SpawnPredators(predatorRandom, tankCenter, tankExtents);
        }

        Vector3 centerRange = GetSchoolCenterRange(tankExtents);
        Vector3 schoolCenter = tankCenter + RandomInBox(random, centerRange);
        Vector3 initialSchoolForward = CreateInitialSchoolForward(random);
        List<Vector3> placedPositions = new List<Vector3>(numberOfFish);

        for (int i = 0; i < numberOfFish; i++)
        {
            Vector3 startPosition = FindSpawnPosition(random, schoolCenter, tankCenter, tankExtents, i, placedPositions);
            placedPositions.Add(startPosition);

            Transform fishTransform = CreateFishRoot(i + 1);
            fishTransform.position = startPosition;
            fishTransform.rotation = Quaternion.LookRotation(initialSchoolForward, Vector3.up);

            FishMovement fishMovement = fishTransform.GetComponent<FishMovement>();
            if (fishMovement != null)
            {
                fishMovement.SetTankBounds(tankCenter, tankExtents);
            }
        }
    }

    private Transform CreateFishRoot(int fishNumber)
    {
        if (fishPrefab != null)
        {
            GameObject fishObject = Instantiate(fishPrefab, fishRoot);
            fishObject.name = "SchoolFish_" + fishNumber;
            fishObject.transform.localPosition = Vector3.zero;
            return fishObject.transform;
        }

        GameObject fishRootObject = new GameObject("SchoolFish_" + fishNumber);
        fishRootObject.transform.SetParent(fishRoot, false);
        CreateFishVisual(fishRootObject.transform);
        fishRootObject.AddComponent<FishMovement>();
        return fishRootObject.transform;
    }

    private GameObject CreateFishVisual(Transform parent)
    {
        GameObject visualRoot = new GameObject("Visual");
        visualRoot.transform.SetParent(parent, false);

        CreateFallbackPart(visualRoot.transform, PrimitiveType.Sphere, new Vector3(0f, 0f, 0.05f), new Vector3(0.34f, 0.2f, 0.62f));
        CreateFallbackPart(visualRoot.transform, PrimitiveType.Cube, new Vector3(0f, 0f, -0.28f), new Vector3(0.24f, 0.18f, 0.04f));
        CreateFallbackPart(visualRoot.transform, PrimitiveType.Cube, new Vector3(0f, 0.1f, -0.06f), new Vector3(0.04f, 0.12f, 0.16f));

        return visualRoot;
    }

    private void SpawnPredators(System.Random random, Vector3 tankCenter, Vector3 tankExtents)
    {
        if (predatorPrefab == null)
        {
            return;
        }

        float safeSpacing = Mathf.Max(0f, predatorMinimumSpacing);
        List<Vector3> placedPredatorPositions = new List<Vector3>(numberOfPredators);

        for (int i = 0; i < numberOfPredators; i++)
        {
            Vector3 spawnPosition = FindPredatorSpawnPosition(random, tankCenter, tankExtents, safeSpacing, placedPredatorPositions);
            placedPredatorPositions.Add(spawnPosition);

            GameObject predatorObject = new GameObject("Predator_" + (i + 1));
            predatorObject.transform.SetParent(fishRoot, false);
            predatorObject.transform.position = spawnPosition;

            Vector3 initialForward = tankCenter - spawnPosition;
            if (initialForward.sqrMagnitude <= 0.0001f)
            {
                initialForward = CreateInitialSchoolForward(random);
            }

            predatorObject.transform.rotation = Quaternion.LookRotation(initialForward.normalized, Vector3.up);

            GameObject predatorVisual = Instantiate(predatorPrefab, predatorObject.transform);
            predatorVisual.name = "PredatorVisual";
            predatorVisual.transform.localPosition = Vector3.zero;
            predatorVisual.transform.localRotation = Quaternion.Euler(predatorVisualRotationEuler);

            PredatorMovement predatorMovement = predatorObject.GetComponent<PredatorMovement>();
            if (predatorMovement == null)
            {
                predatorMovement = predatorObject.AddComponent<PredatorMovement>();
            }

            predatorMovement.SetTankBounds(tankCenter, tankExtents);
        }
    }

    private Vector3 FindPredatorSpawnPosition(System.Random random, Vector3 tankCenter, Vector3 tankExtents, float minimumSpacing, List<Vector3> placedPositions)
    {
        int attempts = Mathf.Max(1, spawnPositionAttempts);
        Vector3 bestPosition = ClampToTank(tankCenter + RandomInBox(random, tankExtents), tankCenter, tankExtents);
        float bestNearestDistance = GetNearestNeighborDistance(bestPosition, placedPositions);

        if (minimumSpacing <= 0f || placedPositions.Count == 0)
        {
            return bestPosition;
        }

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector3 candidate = ClampToTank(tankCenter + RandomInBox(random, tankExtents), tankCenter, tankExtents);
            float nearestDistance = GetNearestNeighborDistance(candidate, placedPositions);

            if (nearestDistance >= minimumSpacing)
            {
                return candidate;
            }

            if (nearestDistance > bestNearestDistance)
            {
                bestNearestDistance = nearestDistance;
                bestPosition = candidate;
            }
        }

        return bestPosition;
    }

    private static GameObject CreateFallbackPart(Transform parent, PrimitiveType primitiveType, Vector3 localPosition, Vector3 localScale)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = primitiveType.ToString();
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        return part;
    }

    private void ClearExistingFish()
    {
        if (fishRoot == null)
        {
            return;
        }

        for (int i = fishRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(fishRoot.GetChild(i).gameObject);
        }
    }

    private bool TryGetTankBounds(out Vector3 center, out Vector3 extents)
    {
        center = Vector3.zero;
        extents = Vector3.one;

        if (tankBoundsSource == null)
        {
            return false;
        }

        center = tankBoundsSource.position;
        extents = Vector3.Scale(tankBoundsSource.lossyScale, Vector3.one * 0.5f) - tankPadding;
        extents.x = Mathf.Max(0.6f, extents.x);
        extents.y = Mathf.Max(0.6f, extents.y);
        extents.z = Mathf.Max(0.6f, extents.z);
        return true;
    }

    private Vector3 GetSchoolCenterRange(Vector3 tankExtents)
    {
        Vector3 range = tankExtents - new Vector3(schoolRadius, schoolRadius * 0.45f, schoolRadius);
        range.x = Mathf.Max(0.4f, range.x);
        range.y = Mathf.Max(0.35f, range.y);
        range.z = Mathf.Max(0.4f, range.z);
        return range;
    }

    private Vector3 CreateFormationOffset(System.Random random, int index)
    {
        float normalizedIndex = (index + 0.5f) / Mathf.Max(1, numberOfFish);
        float angle = index * 2.39996323f;
        float radius = schoolRadius * Mathf.Sqrt(normalizedIndex);

        Vector3 offset = new Vector3(
            Mathf.Cos(angle) * radius,
            Mathf.Lerp(-schoolRadius * 0.22f, schoolRadius * 0.22f, (float)random.NextDouble()),
            Mathf.Sin(angle) * radius);

        offset += RandomInBox(random, new Vector3(0.2f, 0.12f, 0.2f));
        return offset;
    }

    private Vector3 FindSpawnPosition(
        System.Random random,
        Vector3 schoolCenter,
        Vector3 tankCenter,
        Vector3 tankExtents,
        int index,
        List<Vector3> placedPositions)
    {
        float safeMinimumSpacing = Mathf.Max(0f, minimumSpawnSpacing);
        int attempts = Mathf.Max(1, spawnPositionAttempts);
        Vector3 bestPosition = ClampToTank(schoolCenter + CreateFormationOffset(random, index), tankCenter, tankExtents);
        float bestNearestDistance = GetNearestNeighborDistance(bestPosition, placedPositions);

        if (safeMinimumSpacing <= 0f || placedPositions.Count == 0)
        {
            return bestPosition;
        }

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector3 candidateOffset = CreateFormationOffset(random, index);
            float retrySpread = safeMinimumSpacing * (0.35f + (0.08f * attempt));
            candidateOffset += RandomInBox(random, new Vector3(retrySpread, retrySpread * 0.45f, retrySpread));

            Vector3 candidatePosition = ClampToTank(schoolCenter + candidateOffset, tankCenter, tankExtents);
            float nearestDistance = GetNearestNeighborDistance(candidatePosition, placedPositions);
            if (nearestDistance >= safeMinimumSpacing)
            {
                return candidatePosition;
            }

            if (nearestDistance > bestNearestDistance)
            {
                bestNearestDistance = nearestDistance;
                bestPosition = candidatePosition;
            }
        }

        return bestPosition;
    }

    private static float GetNearestNeighborDistance(Vector3 position, List<Vector3> existingPositions)
    {
        if (existingPositions == null || existingPositions.Count == 0)
        {
            return float.PositiveInfinity;
        }

        float nearestDistanceSquared = float.PositiveInfinity;
        for (int i = 0; i < existingPositions.Count; i++)
        {
            float distanceSquared = (position - existingPositions[i]).sqrMagnitude;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
            }
        }

        return Mathf.Sqrt(nearestDistanceSquared);
    }

    private static Vector3 CreateInitialSchoolForward(System.Random random)
    {
        float headingRadians = Mathf.Lerp(0f, Mathf.PI * 2f, (float)random.NextDouble());
        return new Vector3(Mathf.Cos(headingRadians), 0f, Mathf.Sin(headingRadians));
    }

    private static Vector3 RandomInBox(System.Random random, Vector3 extents)
    {
        return new Vector3(
            Mathf.Lerp(-extents.x, extents.x, (float)random.NextDouble()),
            Mathf.Lerp(-extents.y, extents.y, (float)random.NextDouble()),
            Mathf.Lerp(-extents.z, extents.z, (float)random.NextDouble()));
    }

    private static Vector3 ClampToTank(Vector3 position, Vector3 center, Vector3 extents)
    {
        return new Vector3(
            Mathf.Clamp(position.x, center.x - extents.x, center.x + extents.x),
            Mathf.Clamp(position.y, center.y - extents.y, center.y + extents.y),
            Mathf.Clamp(position.z, center.z - extents.z, center.z + extents.z));
    }

    private void UpdateValueLabels()
    {
        if (seedValueText != null)
        {
            int displayedSeed = seedSlider != null ? Mathf.RoundToInt(seedSlider.value) : seed;
            seedValueText.text = displayedSeed.ToString();
        }

        if (fishCountValueText != null)
        {
            int displayedFishCount = fishCountSlider != null ? Mathf.RoundToInt(fishCountSlider.value) : numberOfFish;
            fishCountValueText.text = displayedFishCount.ToString();
        }

        if (sharkCountValueText != null)
        {
            int displayedSharkCount = sharkCountSlider != null ? Mathf.RoundToInt(sharkCountSlider.value) : numberOfPredators;
            sharkCountValueText.text = displayedSharkCount.ToString();
        }
    }

    private void UpdateRightPanelValueLabels()
    {
        if (sharkSpeedValueText != null && sharkSpeedSlider != null)
            sharkSpeedValueText.text = sharkSpeedSlider.value.ToString("F2");
        if (sharkTurnSpeedValueText != null && sharkTurnSpeedSlider != null)
            sharkTurnSpeedValueText.text = sharkTurnSpeedSlider.value.ToString("F2");
        if (fishSpeedValueText != null && fishSpeedSlider != null)
            fishSpeedValueText.text = fishSpeedSlider.value.ToString("F2");
        if (fishTurnSpeedValueText != null && fishTurnSpeedSlider != null)
            fishTurnSpeedValueText.text = fishTurnSpeedSlider.value.ToString("F1");
        if (fishBurstSpeedValueText != null && fishBurstSpeedSlider != null)
            fishBurstSpeedValueText.text = fishBurstSpeedSlider.value.ToString("F2");
        if (avoidRadiusValueText != null && avoidRadiusSlider != null)
            avoidRadiusValueText.text = avoidRadiusSlider.value.ToString("F1");
        if (avoidStrengthValueText != null && avoidStrengthSlider != null)
            avoidStrengthValueText.text = avoidStrengthSlider.value.ToString("F0");
        if (maxAvoidForceValueText != null && maxAvoidForceSlider != null)
            maxAvoidForceValueText.text = maxAvoidForceSlider.value.ToString("F0");
        if (lateralFleeValueText != null && lateralFleeSlider != null)
            lateralFleeValueText.text = lateralFleeSlider.value.ToString("F2");
    }

    private void SyncTuningToInstances()
    {
        // Sync shark sliders
        if (sharkSpeedSlider != null && sharkTurnSpeedSlider != null)
        {
            float sharkSpeed = sharkSpeedSlider.value;
            float sharkTurn = sharkTurnSpeedSlider.value;
            for (int i = 0; i < PredatorMovement.ActivePredators.Count; i++)
            {
                PredatorMovement predator = PredatorMovement.ActivePredators[i];
                if (predator == null) continue;
                predator.MoveSpeedValue = sharkSpeed;
                predator.TurnSpeedValue = sharkTurn;
            }
        }

        // Sync fish sliders
        if (fishSpeedSlider != null)
        {
            float fSpeed = fishSpeedSlider.value;
            float fTurn = fishTurnSpeedSlider.value;
            float fBurst = fishBurstSpeedSlider.value;
            float aRadius = avoidRadiusSlider.value;
            float aStrength = avoidStrengthSlider.value;
            float aMaxForce = maxAvoidForceSlider.value;
            float lFlee = lateralFleeSlider.value;
            for (int i = 0; i < FishMovement.ActiveFish.Count; i++)
            {
                FishMovement fish = FishMovement.ActiveFish[i];
                if (fish == null) continue;
                fish.IdleSpeed = fSpeed;
                fish.TurnSpeedValue = fTurn;
                fish.BurstSpeed = fBurst;
                fish.PredatorAvoidanceRadiusValue = aRadius;
                fish.PredatorAvoidanceStrengthValue = aStrength;
                fish.MaxPredatorAvoidanceForceValue = aMaxForce;
                fish.LateralFleeBlendValue = lFlee;
            }
        }

        UpdateRightPanelValueLabels();
    }


    private void SetSchoolMovementActive(bool active)
    {
        schoolMovementActive = active;

        if (fishRoot == null)
        {
            return;
        }

        for (int i = 0; i < fishRoot.childCount; i++)
        {
            Transform child = fishRoot.GetChild(i);
            FishMovement fishMovement = child.GetComponent<FishMovement>();
            if (fishMovement != null)
            {
                fishMovement.SetMovementActive(active);
            }

            PredatorMovement predatorMovement = child.GetComponentInChildren<PredatorMovement>();
            if (predatorMovement != null)
            {
                predatorMovement.SetMovementActive(active);
            }
        }
    }

    private static Text CreateText(Transform parent, Font font, string value, int fontSize, TextAnchor anchor, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject textObject = new GameObject(value, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(size.x, size.y * HudTextScale);

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = Mathf.RoundToInt(fontSize * HudTextScale);
        text.alignment = anchor;
        text.color = Color.white;
        text.text = value;
        return text;
    }

    private Slider CreateLabeledSlider(Transform parent, Font font, string label, int rowIndex, float minValue, float maxValue, out Text valueText)
    {
        Vector2 rowPosition = HudLayout.GetRowPosition(rowIndex);
        CreateText(parent, font, label, 14, TextAnchor.MiddleLeft, rowPosition, HudLayout.LabelSize);
        Slider slider = CreateSlider(parent, rowPosition + HudLayout.SliderOffset, HudLayout.SliderSize, minValue, maxValue);
        valueText = CreateText(parent, font, "0", 14, TextAnchor.MiddleRight, rowPosition + HudLayout.ValueTextOffset, HudLayout.ValueTextSize);
        return slider;
    }

    private static Button CreateButton(Transform parent, Font font, string label, Vector2 anchoredPosition, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = size;

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.17f, 0.24f, 0.35f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;

        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 1f);
        colors.highlightedColor = new Color(0.9f, 0.96f, 1f, 1f);
        colors.pressedColor = new Color(0.78f, 0.88f, 0.98f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(buttonObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text buttonText = textObject.GetComponent<Text>();
        buttonText.font = font;
        buttonText.fontSize = Mathf.RoundToInt(14f * HudTextScale);
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.color = Color.white;
        buttonText.text = label;
        buttonText.raycastTarget = false;

        return button;
    }

    private static Slider CreateSlider(Transform parent, Vector2 anchoredPosition, Vector2 size, float minValue, float maxValue)
    {
        GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(parent, false);

        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 1f);
        sliderRect.anchorMax = new Vector2(0f, 1f);
        sliderRect.pivot = new Vector2(0f, 1f);
        sliderRect.anchoredPosition = anchoredPosition;
        sliderRect.sizeDelta = size;

        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundObject.transform.SetParent(sliderObject.transform, false);
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.25f);
        backgroundRect.anchorMax = new Vector2(1f, 0.75f);
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image background = backgroundObject.GetComponent<Image>();
        background.color = new Color(0.17f, 0.24f, 0.35f, 1f);

        GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaObject.transform.SetParent(sliderObject.transform, false);
        RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0f);
        fillAreaRect.anchorMax = new Vector2(1f, 1f);
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = new Vector2(-8f, 0f);

        GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(fillAreaObject.transform, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = fillObject.GetComponent<Image>();
        fillImage.color = new Color(0.22f, 0.68f, 0.95f, 1f);

        GameObject handleAreaObject = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaObject.transform.SetParent(sliderObject.transform, false);
        RectTransform handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0f, 0f);
        handleAreaRect.anchorMax = new Vector2(1f, 1f);
        handleAreaRect.offsetMin = new Vector2(8f, 0f);
        handleAreaRect.offsetMax = new Vector2(-8f, 0f);

        GameObject handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleObject.transform.SetParent(handleAreaObject.transform, false);
        RectTransform handleRect = handleObject.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0.5f, 0.5f);
        handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(14f, 22f);
        Image handleImage = handleObject.GetComponent<Image>();
        handleImage.color = Color.white;

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.targetGraphic = handleImage;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.direction = Slider.Direction.LeftToRight;
        slider.SetDirection(Slider.Direction.LeftToRight, true);
        return slider;
    }
}