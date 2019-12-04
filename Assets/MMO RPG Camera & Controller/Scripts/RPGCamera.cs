using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(RPGViewFrustum))]

public class RPGCamera : MonoBehaviour {

	/* For public variable descriptions, please visit https://johnstairs.com/?page_id=2 */
	public Camera UsedCamera;
	public Material UsedSkybox;
	public Vector3 CameraPivotLocalPosition = new Vector3(0, 0.5f, 0);
	public bool ActivateCameraControl = true;
	public bool AlwaysRotateCamera = false;
	public RotateWithCharacter RotateWithCharacter = RotateWithCharacter.RotationStoppingInput;
	public string RotationStoppingInput = "Fire1";
	public CursorLockMode CursorLockMode = CursorLockMode.Confined;
	public bool HideCursorWhenPressed = true;
	public bool LockMouseX = false;
	public bool LockMouseY = false;
	public bool InvertMouseX = false;
	public bool InvertMouseY = true;
	public float MouseXSensitivity = 4.0f;
	public float MouseYSensitivity = 4.0f;
	public bool ConstrainMouseX = false;
	public float MouseXMin = -90.0f;
	public float MouseXMax = 90.0f;
	public float MouseYMin = -89.9f;
	public float MouseYMax = 89.9f;
	public float MouseScrollSensitivity = 15.0f;
	public float MouseSmoothTime = 0.1f;
	public float MinDistance = 0;
	public float MaxDistance = 20.0f;
	public float DistanceSmoothTime = 0.7f;
	public float StartMouseX = 0;
	public float StartMouseY = 15.0f;
	public float StartDistance = 7.0f;
	public AlignCharacter AlignCharacter = AlignCharacter.OnAlignmentInput;
	public string AlignCharacterInput = "Fire2";
	public float AlignCharacterSpeed = 10.0f;
	public bool AlignCameraWhenMoving = true;
	public bool SupportWalkingBackwards = true;
	public float AlignCameraSmoothTime = 0.2f;
	public Color UnderwaterFogColor = new Color(0, 0.13f, 0.59f);
	public float UnderwaterFogDensity = 0.06f;
	public float UnderwaterThresholdTuning = 0.16f;

	private Skybox _skybox;
	private bool _skyboxChanged = false;
	// Camera pivot position in world coordinates
	private Vector3 _cameraPivotPosition;
	// Used view frustum script for camera distance/constraints computations
	private RPGViewFrustum _rpgViewFrustum;
	// Reference to the RPGMotor script
	private RPGMotor _rpgMotor;
	// Desired camera position, can be unequal to the current position because of ambient occlusion
	private Vector3 _desiredPosition;
	// Analogous to _desiredPosition
	private float _desiredDistance;
	private float _distanceSmooth = 0;
	private float _distanceCurrentVelocity;
	// If true, automatically align the camera with the character
	private bool _alignCameraWithCharacter = false;
	// Current mouse/camera X rotation
	private float _mouseX = 0;
	private float _mouseXSmooth = 0;
	private float _mouseXCurrentVelocity;
	// Current mouse/camera Y rotation
	private float _mouseY = 0;
	private float _mouseYSmooth = 0;
	private float _mouseYCurrentVelocity;
	// Desired mouse/camera Y rotation, as the Y rotation can be constrained by terrain
	private float _desiredMouseY = 0;
	// If true, the character's Y rotation was already aligned with the camera 
	private bool _characterYrotationAligned = false;
	// If true, the character is currently turning to the direction the camera is facing
	private bool _turningRoutineStarted = false;
	// Currently running character turning routine to fit the camera's Y [and X] axis rotation
	private Coroutine _turningCoroutine;
	// Water height the character is swimming in
	private float _waterHeightWorld = -Mathf.Infinity;
	// True if the camera is currently underwater (used for applying/undo the underwater effect)
	private bool _underwater = false;
	// Project setting's fog color and density values at script awakening (used for underwater effect logic)
	private Color _defaultFogColor;
	private float _defaultFogDensity;
	
	// Enum for starting turning routines only along certain character axes
	public enum TurningRotation {
		BothAxes,
		OnlyYaxis,
		ResetXaxis
	};


	private void Awake() {
		try {
			Input.GetButton("First Person Zoom");
			Input.GetButton("Maximum Distance Zoom");
		} catch (Exception e) {
			Debug.LogWarning(e.Message + ". Please set up all inputs needed for the RPGCamera according to the provided manual.");
			this.enabled = false;
		}

		if (RenderSettings.fog) {
			_defaultFogDensity = RenderSettings.fogDensity;
		} else {
			_defaultFogDensity = 0;
			RenderSettings.fogDensity = 0;
		}
		RenderSettings.fog = true;
		_defaultFogColor = RenderSettings.fogColor;

		// Check if there is a prescribed camera to use
		if (UsedCamera == null) {
			// Create one for usage in the following code
			GameObject camObject = new GameObject(transform.name + transform.GetInstanceID() + " Camera");
			camObject.AddComponent<Camera>();
			camObject.AddComponent<FlareLayer>();
			camObject.AddComponent<Skybox>();
			_skybox = camObject.GetComponent<Skybox>();
			UsedCamera = camObject.GetComponent<Camera>();
			UsedCamera.transform.eulerAngles = new Vector3(0, transform.eulerAngles.y, 0);
		}
		
		_skybox = UsedCamera.GetComponent<Skybox>();
		// Check if the used camera has a skybox attached
		if (_skybox == null) {
			// No skybox attached => add a skybox and assign it to the _skybox variable
			UsedCamera.gameObject.AddComponent<Skybox>();
			_skybox = UsedCamera.gameObject.GetComponent<Skybox>();
		}		
		// Set the used camera's skybox to the user prescribed one
		_skybox.material = UsedSkybox;

		if (AlignCharacterInput == "Vertical") {
			// Prevent side effects
			AlignCameraWhenMoving = false;
		}

		ResetView();
		// Assign the remaining script variables
		_rpgViewFrustum = GetComponent<RPGViewFrustum>();
		_rpgMotor = GetComponent<RPGMotor>();
	}

	private void LateUpdate() {
		// Make AlwaysRotateCamera and AlignCameraWhenMoving mutual exclusive
		if (AlwaysRotateCamera) {
			AlignCameraWhenMoving = false;
		}
		
		// Check if the UsedSkybox variable has been changed through SetUsedSkybox()
		if (_skyboxChanged) {
			// Update the used camera's skybox
			_skybox.material = UsedSkybox;
			_skyboxChanged = false;
		}

		// Check if the camera is underwater
		if (UsedCamera.transform.position.y < _waterHeightWorld + UnderwaterThresholdTuning) {
			// Change the fog settings only once
			if (!_underwater) {
				_underwater = true;

				EnableUnderwaterEffects();
			}
		} else {
			// Change the fog settings only once
			if (_underwater) {
				_underwater = false;

				DisableUnderwaterEffects();
			}
		}

		// Set the camera's pivot position in world coordinates
		_cameraPivotPosition = transform.position + transform.TransformVector(CameraPivotLocalPosition);
		
		// Check if the camera's Y rotation is contrained by terrain
		bool mouseYConstrained = false;
		OcclusionHandling occlusionHandling = _rpgViewFrustum.GetOcclusionHandling();
		List<string> affectingTags = _rpgViewFrustum.GetAffectingTags();
		// Disable the look up feature when the character is swimming
		if ((occlusionHandling == OcclusionHandling.AlwaysZoomIn || occlusionHandling == OcclusionHandling.TagDependent)
			&& !(_rpgMotor != null && _rpgMotor.IsSwimming())) {

			RaycastHit hitInfo;
			mouseYConstrained = Physics.Raycast(UsedCamera.transform.position, Vector3.down, out hitInfo, 1.0f);

			// mouseYConstrained = "Did the ray hit something?" AND "Was it terrain?" AND "Is the camera's Y position under that of the pivot?"
			mouseYConstrained = mouseYConstrained && hitInfo.transform.GetComponent<Terrain>() && UsedCamera.transform.position.y < _cameraPivotPosition.y;

			if (occlusionHandling == OcclusionHandling.TagDependent) {
				// Additionally take into account if the hit transform has a camera affecting tag
				mouseYConstrained = mouseYConstrained && affectingTags.Contains(hitInfo.transform.tag);
			}
		}

		#region Get inputs
		float smoothTime = MouseSmoothTime;
		float mouseYMinLimit = _mouseY;

		#region Get mouse axes inputs
		if (ActivateCameraControl && (Input.GetButton("Fire1") || Input.GetButton("Fire2") || AlwaysRotateCamera)) {
			// Apply the prescribed cursor lock mode and visibility
			Cursor.lockState = CursorLockMode;
			Cursor.visible = !HideCursorWhenPressed;

			#region Mouse X input processing
			if (!LockMouseX) {
				float mouseXinput = 0;

				// Get mouse X axis input
				if (InvertMouseX) {
					mouseXinput = -Input.GetAxis("Mouse X");
				} else {
					mouseXinput = Input.GetAxis("Mouse X");
				}

				if (_rpgMotor != null) {
					// Check if the character should rotate together with the camera
					if (Input.GetButton("Fire2") || AlignCharacter == AlignCharacter.Always || (AlignCharacter == AlignCharacter.OnAlignmentInput && Input.GetButton(AlignCharacterInput))) {
						// Character turning input Fire2 is pressed 
						// OR the character should always align with the camera (and assuming currently is) 
						// OR the character alignment input is pressed (again assuming the character is already aligned or currently aligning)					
						if (_rpgMotor.IsStunned()) {
							// Allow camera to orbit around the stunned character
							_mouseX += mouseXinput * MouseXSensitivity;
						} else if (_turningRoutineStarted) {
							// Character is currently aligning => reset the last Fire2 input inside the motor
							_rpgMotor.SetLocalRotationYinput(0);
						} else {
							// Character not stunned AND no turning to the camera in progress => let the character rotate according to the mouse X axis input
							_rpgMotor.SetLocalRotationYinput(mouseXinput * MouseXSensitivity);
						}					
					} else {
						// No turning input given (anymore)					
						// Reset the last turning input inside the motor
						_rpgMotor.SetLocalRotationYinput(0);
						// Allow the camera to orbit
						_mouseX += mouseXinput * MouseXSensitivity;
					}
				} else {
					// Allow the camera to orbit
					_mouseX += mouseXinput * MouseXSensitivity;
				}

				if (ConstrainMouseX) {
					// Clamp the rotation in X axis direction
					_mouseX = Mathf.Clamp(_mouseX, MouseXMin, MouseXMax);
				}
			}
			#endregion

			#region Mouse Y input processing
			// Get mouse Y axis input
			if (!LockMouseY) {
				if (InvertMouseY) {
					_desiredMouseY -= Input.GetAxis("Mouse Y") * MouseYSensitivity;
				} else {
					_desiredMouseY += Input.GetAxis("Mouse Y") * MouseYSensitivity;
				}

				if (Input.GetButton("Fire2") || AlignCharacter == AlignCharacter.Always || (AlignCharacter == AlignCharacter.OnAlignmentInput && Input.GetButton(AlignCharacterInput))) {
					if (_rpgMotor != null) {
						if (_turningRoutineStarted) {
							// Character is currently aligning => reset the last Fire2 input inside the motor
							_rpgMotor.SetLocalRotationXinput(0);
						} else if (_rpgMotor.IsSwimming() && !_rpgMotor.IsStunned()) {
							_rpgMotor.SetLocalRotationXinput((InvertMouseY ? -Input.GetAxis("Mouse Y") : Input.GetAxis("Mouse Y")) * MouseYSensitivity);
						}
					}
				} else {
					if (_rpgMotor != null) {
						// Reset the last Fire2 input inside the motor because no turning input is given anymore
						_rpgMotor.SetLocalRotationXinput(0);
					}
				}
			}

			// Check if the camera's Y rotation is constrained by terrain
			if (mouseYConstrained) {
				_mouseY = Mathf.Clamp(_desiredMouseY, Mathf.Max(mouseYMinLimit, MouseYMin), MouseYMax);
				// Set the desired mouse Y rotation to compute the degrees of looking up with the camera
				_desiredMouseY = Mathf.Max(_desiredMouseY, _mouseY - 90.0f);
			} else {
				// Clamp the mouse between the maximum values
				_mouseY = Mathf.Clamp(_desiredMouseY, MouseYMin, MouseYMax);
			}

			_desiredMouseY = Mathf.Clamp(_desiredMouseY, MouseYMin, MouseYMax);
			#endregion
		} else {
			// Unlock the cursor and make it visible again
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;

			if (_rpgMotor != null) {
				// Reset the last turning input inside the motor
				_rpgMotor.SetLocalRotationYinput(0);
				_rpgMotor.SetLocalRotationXinput(0);
			}
		}
		#endregion

		#region Character alignment, i.e. starting a turning routine
		// Check the character alignment mode
		if (_rpgMotor != null && !_rpgMotor.IsStunned()) {

			bool startDiving = Input.GetButtonDown("Vertical")
								|| Input.GetButton("Fire1") && Input.GetButtonDown("Fire2")
								|| Input.GetButtonDown("Fire1") && Input.GetButton("Fire2")
								|| _rpgMotor.StartedAutorunning();

			bool stopDiving = _rpgMotor.DiveOnlyWhenSwimmingForward &&
								(Input.GetButtonUp("Vertical")
								|| (Input.GetButton("Fire2") && Input.GetButtonUp("Fire1"))
								|| _rpgMotor.StoppedAutorunning());

			if (AlignCharacter == AlignCharacter.Always) {
				if (!_characterYrotationAligned) {
					StartTurningCoroutine(TurningCoroutine(TurningRotation.OnlyYaxis));
					_characterYrotationAligned = true;
				}

				if (_rpgMotor.IsSwimming()) {
					if (startDiving) {
						StartTurningCoroutine(TurningCoroutine(TurningRotation.BothAxes));
					} else if (stopDiving) {
						StartTurningCoroutine(TurningCoroutine(TurningRotation.ResetXaxis));
					}
				}
				
			} else if (AlignCharacter == AlignCharacter.OnAlignmentInput) {
				if (_rpgMotor.IsSwimming()) {
					IEnumerator newTurningCoroutine = null;
					if (Input.GetButtonDown(AlignCharacterInput)) {
						newTurningCoroutine = TurningCoroutine(TurningRotation.OnlyYaxis);
					}

					if (Input.GetButton(AlignCharacterInput) && startDiving) {
						newTurningCoroutine = TurningCoroutine(TurningRotation.BothAxes);
					} else if (stopDiving) {
						newTurningCoroutine = TurningCoroutine(TurningRotation.ResetXaxis);
					} else if (Input.GetButton("Vertical") && Input.GetButtonDown(AlignCharacterInput)) {
						newTurningCoroutine = TurningCoroutine(TurningRotation.BothAxes);
					}

					StartTurningCoroutine(newTurningCoroutine);
				} else {
					if (Input.GetButtonDown(AlignCharacterInput) && !_characterYrotationAligned) {
						StartTurningCoroutine(TurningCoroutine(TurningRotation.OnlyYaxis));
						_characterYrotationAligned = true;
					} else if (Input.GetButtonUp(AlignCharacterInput)) {
						_characterYrotationAligned = false;
					}
				}

			} else if (AlignCharacter == AlignCharacter.Never) {
				if (_rpgMotor.IsSwimming() && stopDiving) {
					StartTurningCoroutine(TurningCoroutine(TurningRotation.ResetXaxis));
				}
			}
		}
		#endregion

		// Check if the camera should not rotate with the character
		if (_rpgMotor != null && Input.GetAxisRaw("Horizontal") != 0 && !Input.GetButton("Fire2")) {
			// The character turns and does not strafe via Fire2 => check the RotateWithCharacter value
			if (RotateWithCharacter == RotateWithCharacter.Never || (RotateWithCharacter == RotateWithCharacter.RotationStoppingInput && Input.GetButton(RotationStoppingInput))) {
				// Counter the character's rotation so that the camera stays in place
				float deltaX = Input.GetAxisRaw("Horizontal") * _rpgMotor.GetRotatingSpeed() * 100.0f * Time.deltaTime;
				_mouseX -= deltaX;
				_mouseXSmooth -= deltaX;
			}
		}

		#region Get scroll wheel input
		if (ActivateCameraControl) {
			// Get scroll wheel input
			_desiredDistance = _desiredDistance - Input.GetAxis("Mouse ScrollWheel") * MouseScrollSensitivity;
			_desiredDistance = Mathf.Clamp(_desiredDistance, MinDistance, MaxDistance);
		
			// Check if one of the switch buttons is pressed
			if (Input.GetButton("First Person Zoom")) {
				_desiredDistance = MinDistance;
			} else if (Input.GetButton("Maximum Distance Zoom")) {
				_desiredDistance = MaxDistance;
			}
		}
		#endregion

		#region Camera alignment, e.g. when the character moves forward
		if (_rpgMotor != null) {
			// Get the input direction set by the controller
			Vector3 playerDirection = _rpgMotor.GetPlayerDirection();
			// Set _alignCameraWithCharacter. If true, allow alignment of the camera with the character
			_alignCameraWithCharacter = SetAlignCameraWithCharacter(playerDirection.z != 0 || playerDirection.x != 0);
			if (AlignCameraWhenMoving && _alignCameraWithCharacter) {
				// Alignment is desired and an action occured which should result in an alignment => align the camera
				AlignCameraWithCharacter(!SupportWalkingBackwards || playerDirection.z > 0 || playerDirection.x != 0);
			}
		}
		#endregion
		#endregion

		#region Smooth the inputs
		if (AlignCameraWhenMoving && _alignCameraWithCharacter) {
			smoothTime = AlignCameraSmoothTime;
		}

		_mouseXSmooth = Mathf.SmoothDamp(_mouseXSmooth, _mouseX, ref _mouseXCurrentVelocity, smoothTime);
		_mouseYSmooth = Mathf.SmoothDamp(_mouseYSmooth, _mouseY, ref _mouseYCurrentVelocity, smoothTime);
		#endregion

		#region Compute the new camera position
		Vector3 newCameraPosition;
		// Compute the desired position
		_desiredPosition = GetCameraPosition(_mouseYSmooth, _mouseXSmooth, _desiredDistance);
		// Compute the closest possible camera distance by checking if there is something inside the view frustum
		float closestDistance = _rpgViewFrustum.CheckForOcclusion(_desiredPosition, _cameraPivotPosition, UsedCamera);
		
		if (closestDistance != -1) {
			// Camera view is constrained => set the camera distance to the closest possible distance 
			closestDistance -= UsedCamera.nearClipPlane;
			if (_distanceSmooth < closestDistance) {
				// Smooth the distance if we move from a smaller constrained distance to a bigger constrained distance
				_distanceSmooth = Mathf.SmoothDamp(_distanceSmooth, closestDistance, ref _distanceCurrentVelocity, DistanceSmoothTime);
			} else {
				// Do not smooth if the new closest distance is smaller than the current distance
				_distanceSmooth = closestDistance;
			}
		
		} else {
			// The camera view at the desired position is not contrained but we have to check if it is when zooming to the desired position
			Vector3 currentCameraPosition = GetCameraPosition(_mouseYSmooth, _mouseXSmooth, _distanceSmooth);
			// Check again for occlusion. This time for the current camera position
			closestDistance = _rpgViewFrustum.CheckForOcclusion(currentCameraPosition, _cameraPivotPosition, UsedCamera);

			if (closestDistance != -1) {
				// The camera is/will be constrained on the way to the desired position => set the camera distance to the closest possible distance 
				closestDistance -= UsedCamera.nearClipPlane;
				_distanceSmooth = closestDistance;
			} else {
				// The camera is not constrained on the way to the desired position => smooth the distance change
				_distanceSmooth = Mathf.SmoothDamp(_distanceSmooth, _desiredDistance, ref _distanceCurrentVelocity, DistanceSmoothTime);
			}
		}
		// Compute the new camera position
		newCameraPosition = GetCameraPosition(_mouseYSmooth, _mouseXSmooth, _distanceSmooth);		
		#endregion

		#region Update the camera transform
		UsedCamera.transform.position = newCameraPosition;
		// Check if we are in third or first person and adjust the camera rotation behavior
		if (_distanceSmooth > 0.1f) {
			// In third person => orbit camera
			UsedCamera.transform.LookAt(_cameraPivotPosition);
		} else {
			// In first person => normal camera rotation
			Quaternion characterRotation = Quaternion.Euler(new Vector3(0, transform.eulerAngles.y, 0));
			Quaternion cameraRotation = Quaternion.Euler(new Vector3(_mouseYSmooth, _mouseXSmooth, 0));
			UsedCamera.transform.rotation = characterRotation * cameraRotation;
		}

		if (mouseYConstrained /*|| _distanceSmooth <= 0.1f*/) {
			// Camera lies on terrain => enable looking up			
			float lookUpDegrees = _desiredMouseY - _mouseY;
			UsedCamera.transform.Rotate(Vector3.right, lookUpDegrees);
		}
		#endregion
	}
	
	/* Compute the camera position with rotation around the X axis by xAxisDegrees degrees, around 
	 * the Y axis by yAxisdegrees and with distance distance relative to the direction the 
	 * character is facing */
	private Vector3 GetCameraPosition(float xAxisDegrees, float yAxisDegrees, float distance) {
		Vector3 offset = Vector3.zero;

		// Project the character's X axis onto the X-Z plane
		Vector3 charXaxisMappedToGroundLayer = transform.right;
		charXaxisMappedToGroundLayer.y = 0;
		charXaxisMappedToGroundLayer.Normalize();

		// Retrieve the projected, negative forward vector of the character
		offset = Vector3.Cross(Vector3.up, charXaxisMappedToGroundLayer);
		// Apply the given distance
		offset *= distance;

		// Create the combined rotation of X and Y axis rotation
		Quaternion rotXaxis = Quaternion.AngleAxis(xAxisDegrees, charXaxisMappedToGroundLayer);
		Quaternion rotYaxis = Quaternion.AngleAxis(yAxisDegrees, Vector3.up);
		Quaternion rotation = rotYaxis * rotXaxis;

		return _cameraPivotPosition + rotation * offset;
	}

	/* Resets the camera view behind the character + starting X rotation, starting Y rotation and starting distance StartDistance */
	public void ResetView() {
		_mouseX = StartMouseX;
		_mouseY = _desiredMouseY = StartMouseY;
		_desiredDistance = StartDistance;
	}

	/* Rotates the camera by degree degrees */
	public void Rotate(float degree) {
		_mouseX += degree;
	}

	/* Sets the private variable _alignCameraWithCharacter depending on if the character is in motion */
	private bool SetAlignCameraWithCharacter(bool characterMoves) {
		// Check if camera controls are activated
		if (ActivateCameraControl) {
			// Align camera with character only when the character moves AND neither "Fire1" nor "Fire2" is pressed
			return characterMoves && !Input.GetButton("Fire1") && !Input.GetButton("Fire2");			
		} else {
			// Only align the camera with the character when the character moves
			return characterMoves;
		}
	}

	/* Aligns the camera with the character depending on behindCharacter. If behindCharacter is true, the camera aligns
	 * behind the character, otherwise it aligns so that it faces the character's front */
	private void AlignCameraWithCharacter(bool behindCharacter) {
		float offsetToCameraRotation = CustomModulo(_mouseX, 360.0f);

		float targetRotation = 180.0f;
		if (behindCharacter) {
			targetRotation = 0;
		}

		if (offsetToCameraRotation == targetRotation) {
			// There is no offset to the camera rotation => no alignment computation required
			return;
		}
	
		int numberOfFullRotations = (int)(_mouseX) / 360;
		
		if (_mouseX < 0) {
			if (offsetToCameraRotation < -180) {
				numberOfFullRotations--;
			} else {				
				targetRotation = -targetRotation;
			}
		} else {
			if (offsetToCameraRotation > 180) {
				// The shortest way to rotate behind the character is to fulfill the current rotation
				numberOfFullRotations++;
				targetRotation = -targetRotation;
			}
		}
		
		_mouseX = numberOfFullRotations * 360.0f + targetRotation;
	}

	/* A custom modulo operation for calculating mod of a negative number as well */
	private float CustomModulo(float dividend, float divisor) {
		if (dividend < 0) {
			return dividend - divisor * Mathf.Ceil(dividend / divisor);	
		} else {
			return dividend - divisor * Mathf.Floor(dividend / divisor);
		}
	}
	
	/* Updates the skybox of the camera UsedCamera */
	public void SetUsedSkybox(Material skybox) {
		// Set the new skybox
		UsedSkybox = skybox;
		// Signal that the skybox changed for the next frame
		_skyboxChanged = true;
	}
	
	/* Update the mouse/camera X rotation */
	public void UpdateMouseX(float mouseX) {
		_mouseX += mouseX;
	}
	
	/* If Gizmos are enabled, this method draws some debugging spheres */
	private void OnDrawGizmos() {
		// Draw the camera pivot at its position
		Gizmos.color = Color.cyan;
		Gizmos.DrawSphere(transform.position + transform.TransformVector(CameraPivotLocalPosition), 0.1f);

		// Draw the camera's possible orbit considering occlusions
		Gizmos.color = Color.white;
		Gizmos.DrawWireSphere(_cameraPivotPosition, _distanceSmooth);
	}

	/* Start a character turning routine for aligning the character's axes with the camera axes.
	 * Can be called from outside and overwrites the running coroutine. */
	public void StartTurningCoroutine(TurningRotation rotationAxes) {
		if (_turningRoutineStarted) {
			StopCoroutine(_turningCoroutine);
		}

		_turningCoroutine = StartCoroutine(TurningCoroutine(rotationAxes));
	}

	/* Start a character turning routine for aligning the character's axes with the camera axes */
	private void StartTurningCoroutine(IEnumerator coroutine) {
		if (coroutine == null) {
			return;
		}

		if (_turningRoutineStarted) {
			StopCoroutine(_turningCoroutine);
		}

		_turningCoroutine = StartCoroutine(coroutine);
	}

	/* Routine for turning the character over AlignCharacterSpeed time according to the given TurningRotation.
	 * TurningRotation.ResetXaxis resets the character's X axis rotation to 0 (e.g. when going back on land),
	 * whereas the other two align the character's forward vector to the camera's forward vector (with and 
	 * without X axis alignment) */
	private IEnumerator TurningCoroutine(TurningRotation rotationAxes) {
		_turningRoutineStarted = true;

		_mouseX = _mouseXSmooth;
		_mouseXCurrentVelocity = 0;

		_mouseY = _mouseYSmooth;
		_mouseYCurrentVelocity = 0;
				
		float targetXrotation = 0;
		float targetYrotation = 0;
		SetCharacterTargetRotations(rotationAxes, ref targetXrotation, ref targetYrotation);

		Vector3 targetRotation = new Vector3(targetXrotation, targetYrotation, 0);
		bool cameraAlignedWithCharacter = IsYrotationAlignedWithCharacter();

		while (Quaternion.Angle(transform.rotation, Quaternion.Euler(targetRotation)) > 1.0f || !cameraAlignedWithCharacter) {
			// The difference between the character's orientation and the camera's orientation is greater than 1 degree
			// OR the camera's Y rotation is not yet aligned with the character's view direction
			Quaternion before = transform.rotation;
			
			// Rotate the character towards the view direction of the camera
			transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(targetRotation), AlignCharacterSpeed * 100.0f * Time.deltaTime);
			
			if (!cameraAlignedWithCharacter) {
				// Camera is not completely aligned yet => get the delta angle resulting from the rotation performed above
				float deltaY = transform.rotation.eulerAngles.y - before.eulerAngles.y;
				// and counter the implicite camera rotation
				_mouseX -= deltaY;
				_mouseXSmooth -= deltaY;
			}			

			yield return null;

			// Update the values for the next loop iteration
			cameraAlignedWithCharacter = IsYrotationAlignedWithCharacter();

			SetCharacterTargetRotations(rotationAxes, ref targetXrotation, ref targetYrotation);
			targetRotation = new Vector3(targetXrotation, targetYrotation, 0);
		}

		_turningRoutineStarted = false;
	}

	/* Set the target rotation euler angles for a turning routine depending on the turning rotation which should be performed */
	private void SetCharacterTargetRotations(TurningRotation rotationAxes, ref float targetXrotation, ref float targetYrotation) {
		if (rotationAxes == TurningRotation.BothAxes) {
			targetXrotation = UsedCamera.transform.eulerAngles.x;
			targetYrotation = UsedCamera.transform.eulerAngles.y;
		} else if (rotationAxes == TurningRotation.OnlyYaxis) {
			targetXrotation = transform.eulerAngles.x;
			targetYrotation = UsedCamera.transform.eulerAngles.y;
		} else if (rotationAxes == TurningRotation.ResetXaxis) {
			targetXrotation = 0;
			targetYrotation = transform.eulerAngles.y;
		}
	}

	/* Returns true if the rotation on the Y axis of the camera is almost equal to the Y rotation of the character */
	private bool IsYrotationAlignedWithCharacter() {
		return Mathf.Abs(CustomModulo(_mouseX, 360.0f)) < 0.1f;
	}

	/* Sets the water height in world coordinates (used for checking if the camera is underwater) */
	public void SetGlobalWaterHeight(float globalWaterHeight) {
		_waterHeightWorld = globalWaterHeight;
	}

	/* Enables the visual underwater effects */
	public void EnableUnderwaterEffects() {
		RenderSettings.fogColor = UnderwaterFogColor;
		RenderSettings.fogDensity = UnderwaterFogDensity;
	}

	/* Disables the visual underwater effects */
	public void DisableUnderwaterEffects() {
		RenderSettings.fogColor = _defaultFogColor;
		RenderSettings.fogDensity = _defaultFogDensity;
	}
}
