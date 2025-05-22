using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
using StarterAssets;
#endif

/// <summary>
/// Struct to hold data for aligning camera
/// </summary>
struct CameraPosition
{
	private Vector3 position;
	private Transform xForm;

	public Vector3 Position { get { return position; } set { position = value; } }
	public Transform XForm { get { return xForm; } set { xForm = value; } }

	public void Init(string camName, Vector3 pos, Transform transform, Transform parent)
	{
		position = pos;
		xForm = transform;
		xForm.name = camName;
		xForm.parent = parent;
		xForm.localPosition = Vector3.zero;
		xForm.localPosition = position;
	}
}

/// <summary>
/// Third Person Camera controller
/// </summary>
[RequireComponent(typeof(BarsEffect))]
public class ThirdPersonCamera : MonoBehaviour
{
    #region Variables (private)

	// Inspector serialized
	[SerializeField]
	private Transform cameraXform;
	[SerializeField]
	private float distanceAway;
	[SerializeField]
	private float distanceAwayMultipler = 1.5f;
	[SerializeField]
	private float distanceUp;
	[SerializeField]
	private float distanceUpMultiplier = 5f;
	[SerializeField]
	private CharacterControllerLogic follow;
	[SerializeField]
	private Transform followXform;
	[SerializeField]
	private float widescreen = 0.2f;
	[SerializeField]
	private float targetingTime = 0.5f;
	[SerializeField]
	private float firstPersonLookSpeed = 3.0f;
	[SerializeField]
	private Vector2 firstPersonXAxisClamp = new Vector2(-70.0f, 90.0f);
	[SerializeField]
	private float fPSRotationDegreePerSecond = 120f;
	[SerializeField]
	private float firstPersonThreshold = 0.5f;
	[SerializeField]
	private float freeThreshold = -0.1f;
	[SerializeField]
	private Vector2 camMinDistFromChar = new Vector2(1f, -0.5f);
	[SerializeField]
	private float rightStickThreshold = 0.1f;
	[SerializeField]
	private const float freeRotationDegreePerSecond = -5f;
	[SerializeField]
	private float mouseWheelSensitivity = 3.0f;
	[SerializeField]
	private float compensationOffset = 0.2f;
	[SerializeField]
	private CamStates startingState = CamStates.Free;

	// Smoothing and damping
	private Vector3 velocityCamSmooth = Vector3.zero;
	[SerializeField]
	private float camSmoothDampTime = 0.1f;
	private Vector3 velocityLookDir = Vector3.zero;
	[SerializeField]
	private float lookDirDampTime = 0.1f;

	// Private global only
	private Vector3 lookDir;
	private Vector3 curLookDir;
	private BarsEffect barEffect;
	private CamStates camState = CamStates.Behind;
	private float xAxisRot = 0.0f;
	private CameraPosition firstPersonCamPos;
	private float lookWeight;
	private const float TARGETING_THRESHOLD = 0.01f;
	private Vector3 savedRigToGoal;
	private float distanceAwayFree;
	private float distanceUpFree;
	private Vector2 rightStickPrevFrame = Vector2.zero;
	private float lastStickMin = float.PositiveInfinity;
	private Vector3 nearClipDimensions = Vector3.zero;
	private Vector3[] viewFrustum;
	private Vector3 characterOffset;
	private Vector3 targetPosition;

	// New Input System
#if ENABLE_INPUT_SYSTEM
	private PlayerInput _playerInput;
#endif
	private StarterAssetsInputs _input;

    #endregion

    #region Properties (public)

	public Transform CameraXform
	{
		get { return this.cameraXform; }
	}

	public Vector3 LookDir
	{
		get { return this.curLookDir; }
	}

	public CamStates CamState
	{
		get { return this.camState; }
	}

	public enum CamStates
	{
		Behind,
		FirstPerson,
		Target,
		Free
	}

	public Vector3 RigToGoalDirection
	{
		get
		{
			Vector3 rigToGoalDirection = Vector3.Normalize(characterOffset - this.transform.position);
			rigToGoalDirection.y = 0f;
			return rigToGoalDirection;
		}
	}

	private bool IsCurrentDeviceMouse
	{
		get
		{
#if ENABLE_INPUT_SYSTEM
			return _playerInput.currentControlScheme == "KeyboardMouse";
#else
			return false;
#endif
		}
	}

    #endregion

    #region Unity event functions

	void Start()
	{
		cameraXform = this.transform;
		if (cameraXform == null)
		{
			Debug.LogError("Parent camera to empty GameObject.", this);
		}

		follow = GameObject.FindWithTag("Player").GetComponent<CharacterControllerLogic>();
		followXform = GameObject.FindWithTag("Player").transform;

		lookDir = followXform.forward;
		curLookDir = followXform.forward;

		barEffect = GetComponent<BarsEffect>();
		if (barEffect == null)
		{
			Debug.LogError("Attach a widescreen BarsEffect script to the camera.", this);
		}

		firstPersonCamPos = new CameraPosition();
		firstPersonCamPos.Init
		(
			"First Person Camera",
			new Vector3(0.0f, 1.6f, 0.2f),
			new GameObject().transform,
			follow.transform
		);

		camState = startingState;

		characterOffset = followXform.position + new Vector3(0f, distanceUp, 0f);
		distanceUpFree = distanceUp;
		distanceAwayFree = distanceAway;
		savedRigToGoal = RigToGoalDirection;

		// Initialize Input System
		_input = follow.GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
		_playerInput = follow.GetComponent<PlayerInput>();
#else
		Debug.LogError("Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif
	}

	void OnDrawGizmos()
	{
#if UNITY_EDITOR
		if (EditorApplication.isPlaying && !EditorApplication.isPaused)
		{
			DebugDraw.DrawDebugFrustum(viewFrustum);
		}
#endif
	}

	void LateUpdate()
	{
		viewFrustum = DebugDraw.CalculateViewFrustum(GetComponent<Camera>(), ref nearClipDimensions);

		// Pull values from StarterAssetsInputs
		float rightX = _input.look.x; // Replaces RightStickX
		float rightY = _input.look.y; // Replaces RightStickY
		float leftX = _input.move.x;  // Replaces Horizontal
		float leftY = _input.move.y;  // Replaces Vertical
		float mouseWheel = IsCurrentDeviceMouse ? _input.look.y : 0f; // Map look.y to mouse wheel
		float mouseWheelScaled = mouseWheel * mouseWheelSensitivity;
		float leftTrigger = _input.sprint ? 1f : 0f; // Map sprint to Target
		bool bButtonPressed = _input.jump; // Map jump to ExitFPV
		bool qKeyDown = _input.look.x > 0.5f; // Simulate Q key
		bool eKeyDown = _input.look.x < -0.5f; // Simulate E key
		bool lShiftKeyDown = _input.sprint; // Map sprint to LeftShift

		// Abstraction for mouse input
		if (mouseWheel != 0)
		{
			rightY = mouseWheelScaled;
		}
		if (qKeyDown)
		{
			rightX = 1;
		}
		if (eKeyDown)
		{
			rightX = -1;
		}
		if (lShiftKeyDown)
		{
			leftTrigger = 1;
		}

		characterOffset = followXform.position + (distanceUp * followXform.up);
		Vector3 lookAt = characterOffset;
		targetPosition = Vector3.zero;

		// Determine camera state
		if (leftTrigger > TARGETING_THRESHOLD)
		{
			barEffect.coverage = Mathf.SmoothStep(barEffect.coverage, widescreen, targetingTime);
			camState = CamStates.Target;
		}
		else
		{
			barEffect.coverage = Mathf.SmoothStep(barEffect.coverage, 0f, targetingTime);

			if (rightY > firstPersonThreshold && camState != CamStates.Free && !follow.IsInLocomotion())
			{
				xAxisRot = 0;
				lookWeight = 0f;
				camState = CamStates.FirstPerson;
			}

			if ((rightY < freeThreshold || mouseWheel < 0f) && System.Math.Round(follow.Speed, 2) == 0)
			{
				camState = CamStates.Free;
				savedRigToGoal = Vector3.zero;
			}

			if ((camState == CamStates.FirstPerson && bButtonPressed) ||
			(camState == CamStates.Target && leftTrigger <= TARGETING_THRESHOLD))
			{
				camState = CamStates.Behind;
			}
		}

		follow.Animator.SetLookAtWeight(lookWeight);

		switch (camState)
		{
		case CamStates.Behind:
			ResetCamera();

			if (follow.Speed > follow.LocomotionThreshold && follow.IsInLocomotion() && !follow.IsInPivot())
			{
				lookDir = Vector3.Lerp(followXform.right * (leftX < 0 ? 1f : -1f), followXform.forward * (leftY < 0 ? -1f : 1f), Mathf.Abs(Vector3.Dot(this.transform.forward, followXform.forward)));
				Debug.DrawRay(this.transform.position, lookDir, Color.white);

				curLookDir = Vector3.Normalize(characterOffset - this.transform.position);
				curLookDir.y = 0;
				Debug.DrawRay(this.transform.position, curLookDir, Color.green);

				curLookDir = Vector3.SmoothDamp(curLookDir, lookDir, ref velocityLookDir, lookDirDampTime);
			}

			targetPosition = characterOffset + followXform.up * distanceUp - Vector3.Normalize(curLookDir) * distanceAway;
			Debug.DrawLine(followXform.position, targetPosition, Color.magenta);
			break;

		case CamStates.Target:
			ResetCamera();
			lookDir = followXform.forward;
			curLookDir = followXform.forward;
			targetPosition = characterOffset + followXform.up * distanceUp - lookDir * distanceAway;
			break;

		case CamStates.FirstPerson:
			xAxisRot += (leftY * 0.5f * firstPersonLookSpeed);
			xAxisRot = Mathf.Clamp(xAxisRot, firstPersonXAxisClamp.x, firstPersonXAxisClamp.y);
			firstPersonCamPos.XForm.localRotation = Quaternion.Euler(xAxisRot, 0, 0);

			Quaternion rotationShift = Quaternion.FromToRotation(this.transform.forward, firstPersonCamPos.XForm.forward);
			this.transform.rotation = rotationShift * this.transform.rotation;

			follow.Animator.SetLookAtPosition(firstPersonCamPos.XForm.position + firstPersonCamPos.XForm.forward);
			lookWeight = Mathf.Lerp(lookWeight, 1.0f, Time.deltaTime * firstPersonLookSpeed);

			Vector3 rotationAmount = Vector3.Lerp(Vector3.zero, new Vector3(0f, fPSRotationDegreePerSecond * (leftX < 0f ? -1f : 1f), 0f), Mathf.Abs(leftX));
			Quaternion deltaRotation = Quaternion.Euler(rotationAmount * Time.deltaTime);
			follow.transform.rotation = (follow.transform.rotation * deltaRotation);

			targetPosition = firstPersonCamPos.XForm.position;

			lookAt = Vector3.Lerp(targetPosition + followXform.forward, this.transform.position + this.transform.forward, camSmoothDampTime * Time.deltaTime);
			Debug.DrawRay(Vector3.zero, lookAt, Color.black);
			Debug.DrawRay(Vector3.zero, targetPosition + followXform.forward, Color.white);
			Debug.DrawRay(Vector3.zero, firstPersonCamPos.XForm.position + firstPersonCamPos.XForm.forward, Color.cyan);

			lookAt = Vector3.Lerp(this.transform.position + this.transform.forward, lookAt, Vector3.Distance(this.transform.position, firstPersonCamPos.XForm.position));
			break;

		case CamStates.Free:
			lookWeight = Mathf.Lerp(lookWeight, 0.0f, Time.deltaTime * firstPersonLookSpeed);

			Vector3 rigToGoal = characterOffset - cameraXform.position;
			rigToGoal.y = 0f;
			Debug.DrawRay(cameraXform.transform.position, rigToGoal, Color.red);

			if (rightY < lastStickMin && rightY < -1f * rightStickThreshold && rightY <= rightStickPrevFrame.y && Mathf.Abs(rightX) < rightStickThreshold)
			{
				distanceUpFree = Mathf.Lerp(distanceUp, distanceUp * distanceUpMultiplier, Mathf.Abs(rightY));
				distanceAwayFree = Mathf.Lerp(distanceAway, distanceAway * distanceAwayMultipler, Mathf.Abs(rightY));
				targetPosition = characterOffset + followXform.up * distanceUpFree - RigToGoalDirection * distanceAwayFree;
				lastStickMin = rightY;
			}
			else if (rightY > rightStickThreshold && rightY >= rightStickPrevFrame.y && Mathf.Abs(rightX) < rightStickThreshold)
			{
				distanceUpFree = Mathf.Lerp(Mathf.Abs(transform.position.y - characterOffset.y), camMinDistFromChar.y, rightY);
				distanceAwayFree = Mathf.Lerp(rigToGoal.magnitude, camMinDistFromChar.x, rightY);
				targetPosition = characterOffset + followXform.up * distanceUpFree - RigToGoalDirection * distanceAwayFree;
				lastStickMin = float.PositiveInfinity;
			}

			if (rightX != 0 || rightY != 0)
			{
				savedRigToGoal = RigToGoalDirection;
			}

			cameraXform.RotateAround(characterOffset, followXform.up, freeRotationDegreePerSecond * (Mathf.Abs(rightX) > rightStickThreshold ? rightX : 0f));

			if (targetPosition == Vector3.zero)
			{
				targetPosition = characterOffset + followXform.up * distanceUpFree - savedRigToGoal * distanceAwayFree;
			}
			break;
		}

		CompensateForWalls(characterOffset, ref targetPosition);
		SmoothPosition(cameraXform.position, targetPosition);
		transform.LookAt(lookAt);

		rightStickPrevFrame = new Vector2(rightX, rightY);
	}

    #region Methods

	private void SmoothPosition(Vector3 fromPos, Vector3 toPos)
	{
		cameraXform.position = Vector3.SmoothDamp(fromPos, toPos, ref velocityCamSmooth, camSmoothDampTime);
	}

	private void CompensateForWalls(Vector3 fromObject, ref Vector3 toTarget)
	{
		RaycastHit wallHit = new RaycastHit();
		if (Physics.Linecast(fromObject, toTarget, out wallHit))
		{
			Debug.DrawRay(wallHit.point, wallHit.normal, Color.red);
			toTarget = wallHit.point;
		}

		Vector3 camPosCache = GetComponent<Camera>().transform.position;
		GetComponent<Camera>().transform.position = toTarget;
		viewFrustum = DebugDraw.CalculateViewFrustum(GetComponent<Camera>(), ref nearClipDimensions);

		for (int i = 0; i < (viewFrustum.Length / 2); i++)
		{
			RaycastHit cWHit = new RaycastHit();
			RaycastHit cCWHit = new RaycastHit();

			while (Physics.Linecast(viewFrustum[i], viewFrustum[(i + 1) % (viewFrustum.Length / 2)], out cWHit) ||
				Physics.Linecast(viewFrustum[(i + 1) % (viewFrustum.Length / 2)], viewFrustum[i], out cCWHit))
			{
				Vector3 normal = wallHit.normal;
				if (wallHit.normal == Vector3.zero)
				{
					if (cWHit.normal == Vector3.zero)
					{
						if (cCWHit.normal == Vector3.zero)
						{
							Debug.LogError("No available geometry normal from near clip plane LineCasts. Something must be amuck.", this);
						}
						else
						{
							normal = cCWHit.normal;
						}
					}
					else
					{
						normal = cWHit.normal;
					}
				}

				toTarget += (compensationOffset * normal);
				GetComponent<Camera>().transform.position += toTarget;

				viewFrustum = DebugDraw.CalculateViewFrustum(GetComponent<Camera>(), ref nearClipDimensions);
			}
		}

		GetComponent<Camera>().transform.position = camPosCache;
		viewFrustum = DebugDraw.CalculateViewFrustum(GetComponent<Camera>(), ref nearClipDimensions);
	}

	private void ResetCamera()
	{
		lookWeight = Mathf.Lerp(lookWeight, 0.0f, Time.deltaTime * firstPersonLookSpeed);
		transform.localRotation = Quaternion.Lerp(transform.localRotation, Quaternion.identity, Time.deltaTime);
	}

    #endregion
}
#endregion