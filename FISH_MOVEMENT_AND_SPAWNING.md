# Fish Movement and Spawning: Full Technical Explanation

This document explains how the current fish system works in detail, including runtime control flow, movement math, schooling, separation, wall avoidance, and spawning. It references the implementation in:

- `Assets/Scripts/FishMovement.cs`
- `Assets/Scripts/FishHudSchoolController.cs`

The explanations below describe the current behavior of the codebase as it exists now.

## 1. High-level architecture

There are two main runtime responsibilities:

1. `FishHudSchoolController` creates the school, places the fish, builds the HUD, and starts/stops motion.
2. `FishMovement` lives on each fish and makes that fish move independently every frame.

So the controller is responsible for **school construction and control**, while each fish is responsible for its **own movement decisions**.

## 2. Main files and important functions

### `Assets/Scripts/FishMovement.cs`

Important functions:

- `OnEnable()` / `OnDisable()`
- `Start()`
- `Update()`
- `SetMovementActive(bool active)`
- `SetTankBounds(Vector3 center, Vector3 extents)`
- `QueueRandomWiggle()`
- `CalculateSchoolingForce()`
- `GetFallbackSeparationDirection(FishMovement other)`
- `CalculateWallAvoidanceForce(Vector3 schoolingForce)`
- `AccumulateWallPressure(...)`
- `GetFallbackWallTangent(Vector3 wallNormal)`
- `IsWithinFollowCone(Vector3 otherPosition)`
- `GetFollowConeOrigin()`
- `GetFollowConeMinimumDot()`
- `GetSafeWiggleInterval()`

### `Assets/Scripts/FishHudSchoolController.cs`

Important functions:

- `ApplySettings()`
- `OnPlayClicked()` / `OnPauseClicked()` / `OnResetClicked()`
- `RebuildSchool()`
- `CreateFishRoot(int fishNumber)`
- `ClearExistingFish()`
- `TryGetTankBounds(out Vector3 center, out Vector3 extents)`
- `GetSchoolCenterRange(Vector3 tankExtents)`
- `CreateFormationOffset(System.Random random, int index)`
- `FindSpawnPosition(...)`
- `GetNearestNeighborDistance(...)`
- `CreateInitialSchoolForward(System.Random random)`
- `RandomInBox(System.Random random, Vector3 extents)`
- `ClampToTank(Vector3 position, Vector3 center, Vector3 extents)`
- `SetSchoolMovementActive(bool active)`

## 3. Runtime lifecycle

### 3.1 Controller startup

`FishHudSchoolController.Start()` calls `ApplySettings()`.

`ApplySettings()` does the following:

1. Clamps the seed and fish count to legal ranges.
2. Updates the HUD slider values and labels.
3. Calls `UnityEngine.Random.InitState(seed)` so Unity random calls are repeatable from the selected seed.
4. Calls `RebuildSchool()` to destroy and recreate the current school.
5. Calls `SetSchoolMovementActive(schoolMovementActive)` so the newly spawned fish match the current play/pause state.

### 3.2 Fish startup

Each fish runs `FishMovement.Start()`.

That function:

1. Initializes `isMoving` from `startMovingOnSpawn` if movement state has not already been manually set.
2. Sets `targetRotation` to the fish's current rotation.
3. Randomizes the starting wiggle timer with `Random.Range(0f, GetSafeWiggleInterval())` so fish are not perfectly synchronized.
4. Resets `wiggleOffsetRotation` to identity.

### 3.3 Per-frame movement

Every frame, `FishMovement.Update()` does this in order:

1. If the fish is paused, it returns immediately.
2. Advances the wiggle timer.
3. If the timer reaches the wiggle interval, it queues a new random yaw/pitch wiggle.
4. Computes `schoolingForce = CalculateSchoolingForce()`.
5. Computes `wallAvoidanceForce = CalculateWallAvoidanceForce(schoolingForce)`.
6. Builds a desired direction:

   `desiredForward = transform.forward + schoolingForce + wallAvoidanceForce`

7. Applies wiggle as a rotation to that desired direction:

   `desiredForward = wiggleOffsetRotation * desiredForward`

8. If the resulting vector is non-zero, it updates `targetRotation` with `Quaternion.LookRotation(...)`.
9. Smoothly rotates toward that target with `Quaternion.Slerp(...)`.
10. Moves forward using:

   `transform.position += transform.forward * (moveSpeed * Time.deltaTime)`

So the fish always advances along its current forward axis after rotating toward a steering target.

## 4. The fish registry and school membership

`FishMovement` maintains a static list:

- `private static readonly List<FishMovement> ActiveFish`

`OnEnable()` adds the fish to this list, and `OnDisable()` removes it.

This means each fish can inspect the positions of all currently active fish without needing a central movement manager.

However, a fish does **not** consider every active fish in the whole scene. In `CalculateSchoolingForce()`, it checks:

- `other != this`
- `other != null`
- `other.transform.parent == transform.parent`

This scopes interactions to fish under the same spawned school root. In other words, fish only school with siblings under the same parent transform.

## 5. Movement parameters and what they mean

The most important serialized parameters in `FishMovement.cs` are:

- `moveSpeed`: forward translation speed.
- `turnSpeed`: rotational responsiveness.
- `wiggleInterval`: time between stochastic direction updates.
- `maxWiggleAngle`: maximum yaw wiggle in degrees.
- `maxPitchWiggleAngle`: maximum pitch wiggle in degrees.
- `schoolingStrength`: overall strength of soft repulsion and cohesion.
- `preferredNeighborDistance`: target neighbor spacing scale used in the attraction/repulsion law.
- `attractionExponent`: controls how attraction grows with distance.
- `repulsionExponent`: controls how soft repulsion grows at short range.
- `interactionRadius`: maximum distance at which schooling interactions are considered.
- `maxSchoolingForce`: cap for the non-hard-separation schooling force.
- `alignmentStrength`: strength of heading matching.
- `separationRadius`: hard no-overlap radius.
- `separationStrength`: strength of the hard separation response.
- `maxSeparationForce`: cap for the hard separation response.
- `followConeAngle`: width of the visual/awareness cone.
- `followConeOriginLocalOffset`: local-space tail offset where the cone begins.
- `wallAvoidanceDistance`: how far from a wall avoidance begins.
- `wallAvoidanceStrength`: how strongly the fish blends into wall-following / inward steering.

## 6. Random wiggle math

The wiggle is generated in `QueueRandomWiggle()`.

Two random angles are drawn:

- `yawWiggleAngle ~ Uniform(-maxWiggleAngle, maxWiggleAngle)`
- `pitchWiggleAngle ~ Uniform(-maxPitchWiggleAngle, maxPitchWiggleAngle)`

Then two quaternions are created:

- yaw around global up: `Quaternion.AngleAxis(yawWiggleAngle, Vector3.up)`
- pitch around the fish's local right axis: `Quaternion.AngleAxis(pitchWiggleAngle, transform.right)`

The final wiggle rotation is:

- `wiggleOffsetRotation = pitchRotation * yawRotation`

This means the fish's desired steering direction gets a small random left/right and up/down tilt at fixed time intervals.

## 7. Perception cone math

The cone logic is implemented in:

- `IsWithinFollowCone(...)`
- `GetFollowConeOrigin()`
- `GetFollowConeMinimumDot()`

### 7.1 Cone origin

The cone does not start at the transform pivot. Instead:

- `GetFollowConeOrigin()` returns `transform.TransformPoint(followConeOriginLocalOffset)`

So if the offset is behind the fish, the cone begins near the tail.

### 7.2 Cone angle test

If `theta = followConeAngle`, then the code computes the half-angle in radians:

- `halfAngle = theta * 0.5 * Deg2Rad`

Then it computes the minimum allowed dot product:

- `minimumDot = cos(halfAngle)`

For a candidate fish, let:

- `toOther = otherPosition - coneOrigin`
- `directionToOther = normalize(toOther)`

The other fish is considered visible if:

- `dot(transform.forward, directionToOther) >= minimumDot`

This is the standard dot-product cone test.

## 8. Schooling, cohesion, alignment, and separation

All of this is computed in `CalculateSchoolingForce()`.

The algorithm has three distinct layers:

1. **Hard separation**: prevents overlapping.
2. **Soft attraction/repulsion**: keeps the school together at a preferred distance scale.
3. **Alignment**: makes fish tend to face similar directions.

### 8.1 Pair geometry

For each other fish in the same school root, define:

- `displacement = self.position - other.position`
- `distance = |displacement|`

If `distance <= 0.0001`, the code treats the fish as effectively co-located.

### 8.2 Hard separation (anti-overlap)

If the distance is effectively zero, the code uses `GetFallbackSeparationDirection(other)` to generate a deterministic pseudo-random escape direction based on instance IDs.

If the fish is within the hard separation radius, the code computes:

- `separationDirection = displacement / distance`
- `separationPressure = 1 - clamp01(distance / separationRadius)`

Then it adds:

- `separationDirection * separationPressure^2`

to `hardSeparationForce`.

Important properties of this design:

- the push direction is normalized, so it does not collapse when fish get extremely close
- the closer the fish are, the stronger the response
- the force is **summed**, not averaged, so crowding increases the separation response instead of weakening it

At the end, hard separation is scaled and clamped independently:

- `separationForce = ClampMagnitude(hardSeparationForce * separationStrength, maxSeparationForce)`

### 8.3 Soft attraction/repulsion law

If the neighbor is within `interactionRadius`, the code computes:

- `normalizedDistance = distance / preferredNeighborDistance`
- `forceScale = normalizedDistance^attractionExponent - normalizedDistance^repulsionExponent`

and then:

- `pairForce = -forceScale * displacement`

Interpretation:

- if the fish are close, the lower exponent dominates and `forceScale <= 0`, which acts like repulsion
- if the fish are far enough apart, the higher exponent dominates and `forceScale > 0`, which acts like attraction

This is a scaled 3D interaction law based on distance from a preferred spacing.

### 8.4 Soft repulsion branch

If `forceScale <= 0`, the pair contributes to `softRepulsionForce`.

This is a softer group-structure force than the dedicated hard separation term. Hard separation handles actual anti-overlap. Soft repulsion helps maintain looser spacing.

### 8.5 Visible-neighbor weighting for cohesion and alignment

If `forceScale > 0`, the neighbor is only used for attraction/alignment if it passes the perception cone test via `IsWithinFollowCone(...)`.

For visible fish, the code computes:

- `toOtherFromConeOrigin = other.position - coneOrigin`
- `coneDistance = |toOtherFromConeOrigin|`
- `forwardDot = dot(transform.forward, toOtherFromConeOrigin / coneDistance)`
- `directionWeight = InverseLerp(minimumDot, 1, forwardDot)`
- `distanceWeight = 1 / (1 + coneDistance)`
- `neighborWeight = directionWeight * distanceWeight`

This means:

- fish more centered in front are weighted more strongly
- closer visible fish are weighted more strongly

The code accumulates:

- weighted neighbor positions
- weighted neighbor forward vectors
- total visible weight

### 8.6 Cohesion

If at least one visible neighbor exists, the local weighted center is:

- `localCenter = weightedNeighborPositionSum / visibleNeighborWeightSum`

Then cohesion is:

- `cohesionForce = (localCenter - self.position) / preferredNeighborDistance`

This pulls the fish toward the local center of the visible group.

### 8.7 Alignment

If at least one visible neighbor exists, the average visible heading is:

- `averageForward = weightedNeighborForwardSum / visibleNeighborWeightSum`

If non-zero, alignment becomes:

- `alignmentForce = normalize(averageForward) * alignmentStrength`

So alignment is a heading bias, not a full position force.

### 8.8 Final schooling result

The code separates the total into two capped parts:

1. **hard separation**
2. **schooling/alignment**

The non-hard-separation schooling force is:

- `schoolingForce = ((softRepulsionForce + cohesionForce) * schoolingStrength) + alignmentForce`
- `schoolingForce = ClampMagnitude(schoolingForce, maxSchoolingForce)`

Then the method returns:

- `return separationForce + schoolingForce`

That design is important because it lets anti-overlap remain strong even when normal schooling is capped.

## 9. Wall avoidance math

Wall avoidance is computed in `CalculateWallAvoidanceForce(Vector3 schoolingForce)`.

The current design does **not** simply push the fish backward from the wall. Instead, it tries to produce a smoother turn that preserves tangential motion.

### 9.1 Distance to each wall

The tank is represented by a center and extents along x, y, and z.

If:

- `localPosition = transform.position - tankCenter`

then the distances to the 6 tank faces are computed as:

- left-side interior distance: `tankExtents.x + localPosition.x`
- right-side interior distance: `tankExtents.x - localPosition.x`
- bottom distance: `tankExtents.y + localPosition.y`
- top distance: `tankExtents.y - localPosition.y`
- back distance: `tankExtents.z + localPosition.z`
- front distance: `tankExtents.z - localPosition.z`

### 9.2 Wall pressure

For each wall within `wallAvoidanceDistance`, `AccumulateWallPressure(...)` computes:

- `x = 1 - clamp01(distanceToWall / wallAvoidanceDistance)`
- `pressure = x^2 * (3 - 2x)`

That is the cubic smoothstep shape. It rises smoothly from 0 to 1 as the fish approaches the wall.

The pressure is used to accumulate an inward wall normal.

### 9.3 Wall-following direction

If the fish is near one or more walls, the summed inward normal is normalized to `wallNormal`.

The code then defines a guidance direction:

- `guidanceDirection = transform.forward + schoolingForce`

This is the fish's preferred motion before wall correction.

Then it projects that guidance direction onto the wall plane:

- `tangentDirection = ProjectOnPlane(guidanceDirection, wallNormal)`

If that degenerates, `GetFallbackWallTangent(wallNormal)` tries:

1. projected `transform.right`
2. projected `transform.up`
3. `Cross(wallNormal, Vector3.up)`
4. `Cross(wallNormal, Vector3.right)`

### 9.4 Final wall steering blend

The desired wall-safe direction is:

- `wallDesiredForward = normalize(normalize(tangentDirection) + 0.35 * wallNormal)`

Interpretation:

- most of the motion remains parallel to the wall
- some inward bias turns the fish back toward the tank interior

The blend factor is:

- `wallBlend = clamp01(maxWallPressure * wallAvoidanceStrength)`

and the returned steering term is:

- `wallAvoidanceForce = (wallDesiredForward - transform.forward) * wallBlend`

So the wall term behaves like a steering correction, not a direct reverse push.

## 10. Final steering composition

The final steering composition in `Update()` is:

1. current forward direction
2. plus schooling / separation force
3. plus wall avoidance steering
4. then rotated by the wiggle quaternion

Mathematically:

- `baseDesired = forward + schooling + wallAvoidance`
- `desiredForward = wiggleRotation * baseDesired`

Then the fish rotates smoothly toward `desiredForward` and translates forward.

This means the stochastic wiggle perturbs the already-structured steering solution, rather than replacing it.

## 11. Spawning system

Spawning is managed by `FishHudSchoolController.RebuildSchool()`.

### 11.1 Tank bounds

`TryGetTankBounds(...)` defines the usable tank volume from `tankBoundsSource`:

- `center = tankBoundsSource.position`
- `extents = tankBoundsSource.lossyScale * 0.5 - tankPadding`

Then each extent is clamped to at least `0.6`.

So the fish do not use the full raw tank mesh size. They use a padded interior region.

### 11.2 School center range

`GetSchoolCenterRange(...)` computes how far the center of the school is allowed to vary:

- `range = tankExtents - (schoolRadius, schoolRadius * 0.45, schoolRadius)`

Then each axis is clamped to a minimum safe value.

The vertical range is intentionally smaller than the horizontal range, which keeps the initial school center away from the very top and bottom.

### 11.3 Random school center

`schoolCenter` is chosen with:

- `schoolCenter = tankCenter + RandomInBox(random, centerRange)`

where `RandomInBox(...)` samples each axis independently with `Mathf.Lerp(-extent, extent, random.NextDouble())`.

### 11.4 Shared initial forward direction

`CreateInitialSchoolForward(...)` chooses a single heading angle:

- `headingRadians ~ Uniform(0, 2π)`
- `initialForward = (cos(heading), 0, sin(heading))`

All fish spawn with this same initial direction, so the school starts aligned.

### 11.5 Base formation pattern

`CreateFormationOffset(...)` distributes fish in a front-facing disk-like pattern around the school center.

For fish index `i`:

- `normalizedIndex = (i + 0.5) / max(1, numberOfFish)`
- `angle = i * 2.39996323`
- `radius = schoolRadius * sqrt(normalizedIndex)`

`2.39996323` radians is the golden-angle style spacing constant. It spreads samples around a disk without obvious radial spokes.

The offset is then:

- `x = cos(angle) * radius`
- `y = lerp(-0.22 * schoolRadius, 0.22 * schoolRadius, rand)`
- `z = sin(angle) * radius`

Then small jitter is added with `RandomInBox(random, new Vector3(0.2f, 0.12f, 0.2f))`.

### 11.6 Minimum spawn spacing

`FindSpawnPosition(...)` improves spawn quality by trying to keep fish apart at creation time.

The algorithm:

1. Generates an initial candidate from `CreateFormationOffset(...)`.
2. Measures its nearest-neighbor distance to already placed fish using `GetNearestNeighborDistance(...)`.
3. If spacing is already acceptable, uses that position.
4. Otherwise, retries several times with gradually larger extra jitter.
5. Returns the first candidate that meets `minimumSpawnSpacing`.
6. If none succeed, returns the candidate with the best nearest-neighbor distance.

This makes spawning robust even when the school is dense.

### 11.7 Tank clamping

Every spawn candidate is passed through `ClampToTank(...)`:

- x clamped to `[center.x - extents.x, center.x + extents.x]`
- y clamped to `[center.y - extents.y, center.y + extents.y]`
- z clamped to `[center.z - extents.z, center.z + extents.z]`

This guarantees no fish spawns outside the allowed tank volume.

### 11.8 Instantiating fish

`CreateFishRoot(int fishNumber)` either:

- instantiates `fishPrefab`, or
- creates a fallback fish GameObject and adds `FishMovement`

After creation, `RebuildSchool()`:

1. sets the fish position
2. sets the common initial rotation
3. calls `fishMovement.SetTankBounds(tankCenter, tankExtents)`

so every fish knows its movement limits.

## 12. Play, pause, and reset behavior

These behaviors are controlled in `FishHudSchoolController`.

### Play

`OnPlayClicked()` calls:

- `SetSchoolMovementActive(true)`

which iterates through children under `fishRoot` and calls `FishMovement.SetMovementActive(true)`.

### Pause

`OnPauseClicked()` calls:

- `SetSchoolMovementActive(false)`

which stops per-frame motion because `FishMovement.Update()` immediately returns when `isMoving == false`.

### Reset

`OnResetClicked()` first sets:

- `schoolMovementActive = false`

and then calls `ApplySettings()`.

This rebuilds the school and leaves it paused.

## 13. Determinism and randomness

Two random systems are involved:

1. `System.Random(seed)` in spawning functions for school layout and school heading.
2. `UnityEngine.Random` in fish wiggle timing and wiggle angles.

Because `ApplySettings()` calls `UnityEngine.Random.InitState(seed)`, resets with the same seed reproduce the same random stream structure for Unity random calls starting from that point.

## 14. Why the current behavior looks the way it does

The current design creates the observed motion for these reasons:

- fish always move forward because translation is always `transform.forward * moveSpeed * dt`
- fish do not move in lockstep because wiggle timers are randomized
- fish can form 3D volume because wiggle includes pitch, not just yaw
- fish stay in a common school because cohesion and alignment are weighted toward visible neighbors
- fish avoid direct overlap because hard separation is normalized and independently capped
- fish remain inside the tank because wall steering curves them along boundaries while biasing them inward
- fish start in a coherent school because spawning uses a common center, common initial heading, and minimum spacing retries

## 15. Summary of the mathematical model

At a high level, each fish is following this logic each frame:

1. Build a local interaction model from nearby siblings.
2. Compute hard anti-overlap separation.
3. Compute soft spacing/cohesion/alignment with visible neighbors.
4. Compute tank-boundary steering.
5. Apply a stochastic yaw/pitch wiggle.
6. Smoothly rotate toward the resulting direction.
7. Move forward.

In compact form:

- `desired = wiggle( forward + separation + schooling + wallAvoidance )`
- `rotation <- slerp(rotation, look(desired), turnSpeed * dt)`
- `position <- position + forward * moveSpeed * dt`

This is why the system behaves like a decentralized school rather than a centrally animated group.