using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using StarterAssets;
#endif

public class CharacterControllerLogic : MonoBehaviour
{
    #region Variables (private)

	// Inspector serialized
	[SerializeField]
	private Animator animator;
	[SerializeField]
	private ThirdPersonCamera gamecam;
	[SerializeField]
	private float rotationDegreePerSecond = 120f;
	[SerializeField]
	private float directionSpeed = 1.5f;
	[SerializeField]
	private float directionDampTime = 0.25f;
	[SerializeField]
	private float speedDampTime = 0.05f;
	[SerializeField]
	private float fovDampTime = 3f;
	[SerializeField]
	private float jumpMultiplier = 1f;
	[SerializeField]
	private CapsuleCollider capCollider;
	[SerializeField]
	private float jumpDist = 1f;

	// Private global only
	private float leftX = 0f;
	private float leftY = 0f;
	private AnimatorStateInfo stateInfo;
	private AnimatorTransitionInfo transInfo;
	private float speed = 0f;
	private float direction = 0f;
	private float charAngle = 0f;
	private const float SPRINT_SPEED = 2.0f;
	private const float SPRINT_FOV = 75.0f;
	private const float NORMAL_FOV = 60.0f;
	private float capsuleHeight;

	// Hashes
	private int m_LocomotionId = 0;
	private int m_LocomotionPivotLId = 0;
	private int m_LocomotionPivotRId = 0;
	private int m_LocomotionPivotLTransId = 0;
	private int m_LocomotionPivotRTransId = 0;

	// New Input System
#if ENABLE_INPUT_SYSTEM
	private PlayerInput _playerInput;
#endif
	private StarterAssetsInputs _input;

    #endregion

    #region Properties (public)

	public Animator Animator
	{
		get { return this.animator; }
	}

	public float Speed
	{
		get { return this.speed; }
	}

	public float LocomotionThreshold { get { return 0.2f; } }

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
		animator = GetComponent<Animator>();
		capCollider = GetComponent<CapsuleCollider>();
		capsuleHeight = capCollider.height;
		_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
		_playerInput = GetComponent<PlayerInput>();
#else
		Debug.LogError("Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

		if (animator.layerCount >= 2)
		{
			animator.SetLayerWeight(1, 1);
		}

		// Hash all animation names
		m_LocomotionId = Animator.StringToHash("Base Layer.Locomotion");
		m_LocomotionPivotLId = Animator.StringToHash("Base Layer.LocomotionPivotL");
		m_LocomotionPivotRId = Animator.StringToHash("Base Layer.LocomotionPivotR");
		m_LocomotionPivotLTransId = Animator.StringToHash("Base Layer.Locomotion -> Base Layer.LocomotionPivotL");
		m_LocomotionPivotRTransId = Animator.StringToHash("Base Layer.Locomotion -> Base Layer.LocomotionPivotR");
	}

	void Update()
	{
		if (animator && gamecam.CamState != ThirdPersonCamera.CamStates.FirstPerson)
		{
			stateInfo = animator.GetCurrentAnimatorStateInfo(0);
			transInfo = animator.GetAnimatorTransitionInfo(0);

			// Handle jump input
			if (_input.jump)
			{
				animator.SetBool("Jump", true);
			}
			else
			{
				animator.SetBool("Jump", false);
			}

			// Pull values from StarterAssetsInputs (replacing Input.GetAxis)
			leftX = _input.move.x; // Horizontal input
			leftY = _input.move.y; // Vertical input

			charAngle = 0f;
			direction = 0f;
			float charSpeed = 0f;

			// Translate controls stick coordinates into world/cam/character space
			StickToWorldspace(this.transform, gamecam.transform, ref direction, ref charSpeed, ref charAngle, IsInPivot());

			// Handle sprint
			if (_input.sprint)
			{
				speed = Mathf.Lerp(speed, SPRINT_SPEED, Time.deltaTime);
				gamecam.GetComponent<Camera>().fieldOfView = Mathf.Lerp(gamecam.GetComponent<Camera>().fieldOfView, SPRINT_FOV, fovDampTime * Time.deltaTime);
			}
			else
			{
				speed = charSpeed;
				gamecam.GetComponent<Camera>().fieldOfView = Mathf.Lerp(gamecam.GetComponent<Camera>().fieldOfView, NORMAL_FOV, fovDampTime * Time.deltaTime);
			}

			animator.SetFloat("Speed", speed, speedDampTime, Time.deltaTime);
			animator.SetFloat("Direction", direction, directionDampTime, Time.deltaTime);

			if (speed > LocomotionThreshold)
			{
				if (!IsInPivot())
				{
					animator.SetFloat("Angle", charAngle);
				}
			}
			if (speed < LocomotionThreshold && Mathf.Abs(leftX) < 0.05f)
			{
				animator.SetFloat("Direction", 0f);
				animator.SetFloat("Angle", 0f);
			}
		}
	}

	void FixedUpdate()
	{
		// Rotate character model if stick is tilted right or left, but only if character is moving in that direction
		if (IsInLocomotion() && gamecam.CamState != ThirdPersonCamera.CamStates.Free && !IsInPivot() && ((direction >= 0 && leftX >= 0) || (direction < 0 && leftX < 0)))
		{
			Vector3 rotationAmount = Vector3.Lerp(Vector3.zero, new Vector3(0f, rotationDegreePerSecond * (leftX < 0f ? -1f : 1f), 0f), Mathf.Abs(leftX));
			Quaternion deltaRotation = Quaternion.Euler(rotationAmount * Time.deltaTime);
			this.transform.rotation = (this.transform.rotation * deltaRotation);
		}

		if (IsInJump())
		{
			float oldY = transform.position.y;
			transform.Translate(Vector3.up * jumpMultiplier * animator.GetFloat("JumpCurve"));
			if (IsInLocomotionJump())
			{
				transform.Translate(Vector3.forward * Time.deltaTime * jumpDist);
			}
			capCollider.height = capsuleHeight + (animator.GetFloat("CapsuleCurve") * 0.5f);
			if (gamecam.CamState != ThirdPersonCamera.CamStates.Free)
			{
				gamecam.transform.Translate(Vector3.up * (transform.position.y - oldY));
			}
		}
	}

	void OnDrawGizmos()
	{
	}

    #endregion

    #region Methods

	public bool IsInJump()
	{
		return (IsInIdleJump() || IsInLocomotionJump());
	}

	public bool IsInIdleJump()
	{
		return animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.IdleJump");
	}

	public bool IsInLocomotionJump()
	{
		return animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.LocomotionJump");
	}

	public bool IsInPivot()
	{
		return stateInfo.nameHash == m_LocomotionPivotLId ||
			stateInfo.nameHash == m_LocomotionPivotRId ||
			transInfo.nameHash == m_LocomotionPivotLTransId ||
			transInfo.nameHash == m_LocomotionPivotRTransId;
	}

	public bool IsInLocomotion()
	{
		return stateInfo.nameHash == m_LocomotionId;
	}

	public void StickToWorldspace(Transform root, Transform camera, ref float directionOut, ref float speedOut, ref float angleOut, bool isPivoting)
	{
		Vector3 rootDirection = root.forward;
		Vector3 stickDirection = new Vector3(leftX, 0, leftY);

		speedOut = stickDirection.sqrMagnitude;

		// Get camera rotation
		Vector3 CameraDirection = camera.forward;
		CameraDirection.y = 0.0f; // kill Y
		Quaternion referentialShift = Quaternion.FromToRotation(Vector3.forward, Vector3.Normalize(CameraDirection));

		// Convert joystick input in Worldspace coordinates
		Vector3 moveDirection = referentialShift * stickDirection;
		Vector3 axisSign = Vector3.Cross(moveDirection, rootDirection);

		Debug.DrawRay(new Vector3(root.position.x, root.position.y + 2f, root.position.z), moveDirection, Color.green);
		Debug.DrawRay(new Vector3(root.position.x, root.position.y + 2f, root.position.z), rootDirection, Color.magenta);
		Debug.DrawRay(new Vector3(root.position.x, root.position.y + 2f, root.position.z), stickDirection, Color.blue);
		Debug.DrawRay(new Vector3(root.position.x, root.position.y + 2.5f, root.position.z), axisSign, Color.red);

		float angleRootToMove = Vector3.Angle(rootDirection, moveDirection) * (axisSign.y >= 0 ? -1f : 1f);
		if (!isPivoting)
		{
			angleOut = angleRootToMove;
		}
		angleRootToMove /= 180f;

		directionOut = angleRootToMove * directionSpeed;
	}

    #endregion
}