using UnityEngine;
using System.Collections;

[RequireComponent(typeof(CharacterController))]

public class RPGMotor : MonoBehaviour {

	/* For public variable descriptions, please visit https://johnstairs.com/?page_id=2 */
	public float WalkSpeed = 2.0f;
	public float RunSpeed = 10.0f;
	public float StrafeSpeed = 10.0f;
	public float AirborneSpeed = 2.0f;
	public float RotatingSpeed = 2.5f;
	public float SwimSpeedMultiplier = 1.0f;
	public float SprintSpeedMultiplier = 2.0f;
	public float BackwardsSpeedMultiplier = 0.2f;
	public float JumpHeight = 10.0f;
	public bool UnlimitedAirborneMoves = false;
	public int AllowedAirborneMoves = 1;
	public float SwimmingStartHeight = 0.3f;
	public bool DiveOnlyWhenSwimmingForward = true;
	public bool MoveWithMovingGround = true;	
	public bool RotateWithRotatingGround = true;	
	public bool GroundObjectAffectsJump = true;
	public float SlidingThreshold = 40.0f;
	public float FallingThreshold = 6.0f;
	public float Gravity = 20.0f;

	private CharacterController _characterController;
	private Animator _animator;
	private MotionState _currentMotionState;
	// Local player direction
	private Vector3 _playerDirection;
	// Player direction in world coordinates
	private Vector3 _playerDirectionWorld;
	private float _localRotation;
	private float _localRotationXinput;
	private float _localRotationYinput;
	private float _localRotationHorizontalInput;
	// True if the character should jump in the current frame
	private bool _jump = false;
	// True if the character is automatically running/moving forward
	private bool _autorunning = false;
	// True if autorunning was started in the current frame
	private bool _autorunningStarted = false;
	// False if autorunning was started in the current frame
	private bool _autorunningStopped = false;
	// True if the character should walk
	private bool _walking = false;
	// True if the character is swimming
	private bool _swimming = false;
	// True if the character should surface
	private bool _surface = false;
	// True if the character is sprinting
	private bool _sprinting = false;
	// True if the character is sliding
	private bool _sliding = false;
	// True if the character is stunned and unable to move
	private bool _stunned = false;
	private bool _allowAirborneMovement = false;
	// Allowed moves while airborne
	private int _airborneMovesCount = 0;
	// True if the character hits another collider while jumping
	private bool _jumpingCollision = false;
	// True if the character performed a jump while it was running
	private bool _runningJump = false;
	// Water height the character is swimming in
	private float _waterHeightWorld = -Mathf.Infinity;
	// The object the character is standing on
	private GameObject _groundObject;
	// The character's position of the last frame in world coordinates
	private Vector3 _lastCharacterPosition;
	// The character's position in ground object coordinates
	private Vector3 _groundObjectLocalPosition;
	// The character's rotation of the last frame
	private Quaternion _lastCharacterRotation;
	// The character's rotation relative to the ground object's rotation
	private Quaternion _groundObjectLocalRotation;
	// Variables for smoothing the transition between standing and turning animation
	private float _turningDirectionSmoothed = 0;
	private float _turningDirectionCurrentVelocity = 0;

	
	private void Awake() {
		_characterController = GetComponent<CharacterController>();
		_animator = GetComponent<Animator>();
		_characterController.slopeLimit = SlidingThreshold;
	}

	public void StartMotor() {

		// Calculate the height the character should start to swim in world coordinates 
		float swimmingStartHeightWorld = transform.position.y + SwimmingStartHeight;
		// Store if the character's global start swimming height is under the current water level 
		_swimming = swimmingStartHeightWorld < _waterHeightWorld;

		if (_characterController.isGrounded || _swimming) {
			// Reset the counter for the number of remaining moves while airborne
			_airborneMovesCount = 0;
			// Reset the running jump flag
			_runningJump = false;
			
			if (_autorunning) {
				_playerDirection.z = 1.0f;
			}

			float resultingSpeed;
			if (_stunned) {
				// Prevent any movement caused by the controller
				_playerDirectionWorld = Vector3.zero;
				resultingSpeed = 0;
			} else {
				// Allow movement from the controller
				// Transform the local movement direction to world space
				_playerDirectionWorld = transform.TransformDirection(_playerDirection);

				// Normalize the player's movement direction
				if (_playerDirectionWorld.magnitude > 1) {
					_playerDirectionWorld = Vector3.Normalize(_playerDirectionWorld);
				}

				#region Calculate the movement speed
				resultingSpeed = RunSpeed;
				// Compute the speed combined of strafe and run speed
				if (_playerDirection.x != 0 || _playerDirection.z != 0) {
					resultingSpeed = (StrafeSpeed * Mathf.Abs(_playerDirection.x)
							+ RunSpeed * Mathf.Abs(_playerDirection.z))
							/ (Mathf.Abs(_playerDirection.x) + Mathf.Abs(_playerDirection.z));
				}

				// Multiply with the swim multiplier if the character is swimming
				if (_swimming) {
					resultingSpeed *= SwimSpeedMultiplier;
				}
				// Multiply with the sprint multiplier if sprinting is active
				if (_sprinting) {
					resultingSpeed *= SprintSpeedMultiplier;
				}
				// Adjust the speed if moving backwards
				if (_playerDirection.z < 0) {
					resultingSpeed *= BackwardsSpeedMultiplier;
				}
				// Adjust the speed if walking is enabled
				if (_walking) {
					resultingSpeed = Mathf.Min(WalkSpeed, resultingSpeed);
				}
				#endregion

				// Apply the resulting movement speed
				_playerDirectionWorld *= resultingSpeed;
			}
			
			
			if (!_swimming) {
				// Apply the falling threshold
				_playerDirectionWorld.y = -FallingThreshold;
			}
			
			if (_swimming) {
				// Prevent that the character can swim above the current water level
				if (_playerDirectionWorld.y * Time.deltaTime + swimmingStartHeightWorld > _waterHeightWorld) {
					// The planned move in Y direction would lead to moving the character above the current water level 
					_playerDirectionWorld.y = _waterHeightWorld - swimmingStartHeightWorld;
				}

				if (_surface) {
					// Character should surface
					_playerDirectionWorld.y = resultingSpeed;
				}
			} else if (_jump) {
				// The character is not swimming and should jump this frame
				_jump = false;

				if (_playerDirection.x != 0 || _playerDirection.z != 0) {
					_runningJump = true;
				}

				// Only jump if we are not sliding
				if (!_stunned) {
					_playerDirectionWorld.y = JumpHeight;
				}
			}

			// Apply sliding (Resets _playerDirectionWorld[.y] in case of sliding)
			ApplySliding();

		} else if (_allowAirborneMovement && !_runningJump && !_stunned) {
			// Allow slight movement while airborne only after a standing jump and if not stunned
			Vector3 playerDirectionWorld = transform.TransformDirection(_playerDirection);
			// Normalize the player's movement direction
			if (_playerDirectionWorld.magnitude > 1) {
				playerDirectionWorld = Vector3.Normalize(playerDirectionWorld);
			}
			// Apply the airborne speed
			playerDirectionWorld *= AirborneSpeed;
			// Set the x and z direction to move the character continuously
			_playerDirectionWorld.x = playerDirectionWorld.x;
			_playerDirectionWorld.z = playerDirectionWorld.z;
		}
		
		if (_jumpingCollision) {
			// Got an airborne collision => prevent further soaring
			_playerDirectionWorld.y = -Gravity * Time.deltaTime;
			// Let this happen only once per collision
			_jumpingCollision = false;
		}

		#region Ground movement/rotation computations
		if (MoveWithMovingGround) {
			// Apply ground/passive movement
			ApplyGroundMovement();
		}

		if (RotateWithRotatingGround) {
			// Apply ground/passive rotation
			ApplyGroundRotation();
		}

		// Check if we have left the last inertial space and if so, reset the ground object
		RaycastHit hit;
		if (GroundObjectAffectsJump && Physics.Raycast(transform.position + Vector3.up, Vector3.down, out hit, JumpHeight)) {
			if (hit.transform.gameObject != _groundObject) {
				// Reset the ground object
				_groundObject = null;
			}
		} else {
			// Reset the ground object
			_groundObject = null;
		}
		#endregion

		if (!_swimming) {
			// Apply gravity only if we are not swimming
			_playerDirectionWorld.y -= Gravity * Time.deltaTime;
		}

		// Move the character
		_characterController.Move(_playerDirectionWorld * Time.deltaTime);

		if (MoveWithMovingGround || RotateWithRotatingGround) {
			// Store the ground object's pose in case there was a collision detected while moving
			StoreGroundObjectPose();
		}

		#region Rotate the character
		// Rotate the character according to the RPG Controller and Fire2 mouse input
		_localRotation = _localRotationHorizontalInput + _localRotationYinput;

		if (!_stunned) {
			// Rotate the character along the global Y axis
			transform.Rotate(Vector3.up * _localRotation, Space.World);

			if (_swimming && (!DiveOnlyWhenSwimmingForward || _playerDirection.z != 0)) {
				// Character is swimming and should dive => clamp the character's local X axis rotation between [-89.5, 89.5] (euler angles)
				if (transform.eulerAngles.x > 180.0f) {
					if (transform.eulerAngles.x + _localRotationXinput < 270.5f) {
						_localRotationXinput = Mathf.Min(270.5f - _localRotationXinput, 270.5f - transform.eulerAngles.x);
					}
				} else {
					if (transform.eulerAngles.x + _localRotationXinput > 89.5f) {
						_localRotationXinput = Mathf.Min(89.5f - _localRotationXinput, 89.5f - transform.eulerAngles.x);
					}
				}

				// Rotate the character along the local X axis
				transform.Rotate(Vector3.right * _localRotationXinput, Space.Self);
			}
		}
		#endregion

		#region Animator communication
		// Determine the current motion state
		_currentMotionState = DetermineMotionState();
		if (_animator) {
			// Pass values important for animation to the animator
			_animator.SetInteger("Motion State", (int)_currentMotionState);
			
			_turningDirectionSmoothed = Mathf.SmoothDamp(_animator.GetFloat("Turning Direction"), _localRotationHorizontalInput, ref _turningDirectionCurrentVelocity, 0.05f);
			_animator.SetFloat("Turning Direction", _turningDirectionSmoothed);

			_animator.SetFloat("Movement Direction X", _playerDirection.x, 0.1f, Time.deltaTime);
			_animator.SetFloat("Movement Direction Z", _playerDirection.z, 0.1f, Time.deltaTime);
		}
		#endregion
	}

	/* Applies passive movement if the character stands on moving ground */
	private void ApplyGroundMovement() {		
		if (_groundObject != null) {
			// Compute the delta between the ground object's position of the last and the current frame
			Vector3 newGlobalPlatformPoint = _groundObject.transform.TransformPoint(_groundObjectLocalPosition);
			Vector3 moveDirection = newGlobalPlatformPoint - _lastCharacterPosition;
			if (moveDirection != Vector3.zero) {
				// Move the character in the move direction
				transform.position += moveDirection;
			}
		}		
	}

	/* Applies passive rotation if the character stands on rotating ground */
	private void ApplyGroundRotation() {
		if (_groundObject != null) {			
			// Compute the delta between the ground object's rotation of the last and the current frame
			Quaternion newGlobalPlatformRotation = _groundObject.transform.rotation * _groundObjectLocalRotation;
			Quaternion rotationDelta = newGlobalPlatformRotation * Quaternion.Inverse(_lastCharacterRotation);			
			// Prevent rotation of the character's y-axis
			rotationDelta = Quaternion.FromToRotation(rotationDelta * transform.up, transform.up) * rotationDelta;
			// Rotate the character by the rotation delta
			transform.rotation = rotationDelta * transform.rotation;
		}
	}

	/* Stores the ground object's pose for the next frame if there is a ground object */
	private void StoreGroundObjectPose() {
		if (_groundObject != null) {
			// Store ground object's position for next frame computations
			_lastCharacterPosition = transform.position;
			_groundObjectLocalPosition = _groundObject.transform.InverseTransformPoint(transform.position);
			
			// Store ground object's rotation for next frame computations
			_lastCharacterRotation = transform.rotation;
			_groundObjectLocalRotation = Quaternion.Inverse(_groundObject.transform.rotation) * transform.rotation; 
		}
	}

	/* Applies sliding to the character if it is standing on too steep terrain  */
	private void ApplySliding() {
		RaycastHit hitInfo;

		// Cast a ray down to the ground to get the ground's normal vector
		//Debug.DrawRay(transform.position, Vector3.down * (_characterController.height * 0.5f + 0.5f), Color.magenta);
		if (Physics.Raycast(transform.position, Vector3.down, out hitInfo, _characterController.height * 0.5f + 0.5f)) {
			Vector3 hitNormal = hitInfo.normal;
			// Compute the slope in degrees
			float slope = Vector3.Angle(hitNormal, Vector3.up);
			// Compute the sliding direction
			Vector3 slidingDirection = new Vector3(hitNormal.x, -hitNormal.y, hitNormal.z);
			// Normalize the sliding direction and make it orthogonal to the hit normal
			Vector3.OrthoNormalize(ref hitNormal, ref slidingDirection);
			// Check if the slope is too steep
			if (slope > SlidingThreshold) {
				_sliding = true;

				if (!_swimming) {
					// Apply sliding force
					_playerDirectionWorld = slidingDirection * slope * 0.2f;
				} else if (_characterController.isGrounded && IsCloseToWaterSurface()){
					// Prevent that the character can walk out of the water on ground he would slide on
					_playerDirectionWorld.y = 0;
				}
			} else {
				_sliding = false;
			}
		}
	}

	/* Determines the current motion state of the character by using set variables */
	private MotionState DetermineMotionState() {
		MotionState result;

		if (_stunned) {
			result = MotionState.Stunned;
		} else if (_swimming) {
			result = MotionState.Swimming;
		} else if (_characterController.isGrounded) {
			 if (_playerDirection.magnitude > 0) {
				if (_walking) {
					result = MotionState.Walking;
				} else if (_sprinting) {
					result = MotionState.Sprinting;
				} else {
					result = MotionState.Running;
				}
			} else if (_sliding) {
				result = MotionState.Falling;
			} else {
				result = MotionState.Standing;
			}
		} else {
			if (_playerDirectionWorld.y >= 0) {
				result = MotionState.Jumping;
			} else {
				result = MotionState.Falling;
			}
		}

		return result;
	}

	/* Lets the character jump in the current frame, only works when character is grounded */		
	public void Jump() {
		if (_characterController.isGrounded) {
			// Only allow jumping when the character is grounded
			_jump = true;
		}
	}
	
	/* Enables/Disables sprinting */
	public void Sprint(bool on) {
		_sprinting = on;
	}

	/* Enables/Disables sprinting with speed "speed" */
	public void Sprint(bool on, float speed) {
		_sprinting = on;
		SprintSpeedMultiplier = speed;
	}
	
	/* Toggles walking */
	public void ToggleWalking(bool toggle) {
		if (toggle) {
			_walking = !_walking;
		}
	}
	
	/* Toggles autorun */
	public void ToggleAutorun(bool toggle) {
		if (toggle) {
			_autorunning = !_autorunning;

			if (_autorunning) {
				_autorunningStarted = true;
			} else {
				_autorunningStopped = true;
			}
		}
	}
	
	/* Cancels autorun */
	public void StopAutorun(bool stop) {
		if (stop && _autorunning) {
			_autorunning = false;
			_autorunningStopped = true;
		}
	}

	/* Sets the character's direction inputted by the player/controller */ 
	public void SetPlayerDirectionInput(Vector3 direction) {
		_playerDirection = direction;
	}

	/* Moves the character in mid air if character is not grounded, an mid air movement key is pressed
	 * and the maximum of mid air moves isn't reached */
	public void MoveInMidAir(bool movement) {
		if (UnlimitedAirborneMoves) {
			_allowAirborneMovement = true;
			return;
		}

		_allowAirborneMovement = false;
		// Allow airborne movement for the current frame and increase the airborne moves counter if we are not grounded
		if (movement && _airborneMovesCount < AllowedAirborneMoves) {
			_allowAirborneMovement = true;
			_airborneMovesCount++;
		}
	}
	
	/* Set the local rotation input around the Y axis done by pressing the horizontal input */
	public void SetLocalRotationHorizontalInput(float rotation) {	
		_localRotationHorizontalInput = rotation * RotatingSpeed * 100.0f * Time.deltaTime;
	}
	
	/* Set the local rotation input around the Y axis done by pressing Fire2 */
	public void SetLocalRotationYinput(float rotation) {
		_localRotationYinput = rotation;
	}

	/* Set the local rotation input around the X axis done by pressing Fire2 */
	public void SetLocalRotationXinput(float rotation) {
		_localRotationXinput = rotation;
	}

	/* Gets this frame's player direction */
	public Vector3 GetPlayerDirection() {
		return _playerDirection;
	}

	/* Gets the current rotating speed */
	public float GetRotatingSpeed() {
		return RotatingSpeed;
	}

	/* "OnControllerColliderHit is called when the controller hits a collider while performing a Move" - Unity Documentation */
	public void OnControllerColliderHit(ControllerColliderHit hit) {
		// Check if we have hit something while jumping
		if (_playerDirectionWorld.y > 0) {
			// Signalize a jumping collision for the next frame
			_jumpingCollision = true;
		}
		
		// Set the ground object only if we are really standing on it
		Vector3 colliderBottom = _characterController.bounds.center;
		colliderBottom.y -= _characterController.bounds.extents.y;
		//Debug.DrawRay(hit.point, hit.normal, Color.yellow);
		if ((_characterController.collisionFlags & CollisionFlags.Below) != 0 
		    && Vector3.Distance(colliderBottom, hit.point) < 0.3f * _characterController.radius) {
			_groundObject = hit.gameObject;
		}
	}

	/* Returns true if the character is currently stunned */
	public bool IsStunned() {
		return _stunned;
	}

	/* Stuns the character for "duration" seconds */
	public void Stun(float duration) {
		StartCoroutine(StunCoroutine(duration));
	}

	/* Coroutine for applying and removing the stun effect */
	private IEnumerator StunCoroutine(float duration) {
		_stunned = true;
		yield return new WaitForSeconds(duration);
		_stunned = false;
	}

	/* If Gizmos are enabled, this method draws some debugging gizmos */
	private void OnDrawGizmos() {
		// Draw the local Swimming Start Height
		Gizmos.color = Color.blue;
		Gizmos.DrawCube(transform.position + new Vector3(0, SwimmingStartHeight, 0), new Vector3(0.7f, 0.01f, 0.7f));
	}

	/* Returns true once if the character started autorunning this frame */
	public bool StartedAutorunning() {
		bool result = _autorunningStarted;
		_autorunningStarted = false;
		return result;
	}

	/* Returns true once if the character stopped autorunning this frame */
	public bool StoppedAutorunning() {
		bool result = _autorunningStopped;
		_autorunningStopped = false;
		return result;
	}

	/* Sets the water height in world coordinates (used for checking if the character should swim) */
	public void SetGlobalWaterHeight(float globalWaterHeight) {
		_waterHeightWorld = globalWaterHeight;
	}

	/* Returns true if the character is swimming */
	public bool IsSwimming() {
		return _swimming;
	}

	/* Sets if the character should surface */
	public void Surface(bool surfacing) {
		_surface = surfacing;
	}

	/* Returns true if the character is close to the currently set water level */
	private bool IsCloseToWaterSurface() {
		return Mathf.Abs(_waterHeightWorld - (transform.position.y + SwimmingStartHeight)) < 0.1f;
	}
}
