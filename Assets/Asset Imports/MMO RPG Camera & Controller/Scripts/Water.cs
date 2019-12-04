using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Water : MonoBehaviour {

	// Height of the water in world coordinates, set in Awake
	private float _globalWaterHeight = 0;

	private void Awake() {
		// Set the global height once
		_globalWaterHeight = transform.position.y + GetComponent<BoxCollider>().size.y * transform.localScale.y * 0.5f;
	}

	public void OnTriggerEnter(Collider other) {
		// Collider entered the water
		RPGMotor rpgMotor = other.GetComponent<RPGMotor>();

		if (rpgMotor != null) {
			rpgMotor.SetGlobalWaterHeight(_globalWaterHeight); // Needed for swimming logic
		}

		RPGCamera rpgCamera = other.GetComponent<RPGCamera>();

		if (rpgCamera != null) {
			rpgCamera.SetGlobalWaterHeight(_globalWaterHeight); // Needed for underwater camera logic
		}
	}

	public void OnTriggerExit(Collider other) {
		// Collider left the water
		RPGMotor rpgMotor = other.GetComponent<RPGMotor>();

		if (rpgMotor != null) {
			rpgMotor.SetGlobalWaterHeight(-Mathf.Infinity); // Needed for swimming logic
		}

		RPGCamera rpgCamera = other.GetComponent<RPGCamera>();

		if (rpgCamera != null) {
			// Start a character turning routine so that his X axis is returned to normal
			rpgCamera.StartTurningCoroutine(RPGCamera.TurningRotation.ResetXaxis);
			rpgCamera.SetGlobalWaterHeight(-Mathf.Infinity); // Needed for underwater camera logic
		}
	}
}
