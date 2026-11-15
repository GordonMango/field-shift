using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FieldShift
{
    /// <summary>Walk the farm in first person. WASD/arrows, mouse look, Shift to run, V to ride the quad bike.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public Camera cam;
        public float walk = 4.2f, run = 9f, bike = 16f, lookSpeed = 0.12f;
        public bool inputEnabled = true;
        public bool onBike;
        CharacterController cc;
        float pitch, vy;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.slopeLimit = 50; cc.stepOffset = 0.5f;
        }

        public void Place(Vector3 pos, float yaw)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            pitch = 0;
            cc.enabled = true;
        }

        void Update()
        {
            Vector2 move = Vector2.zero, look = Vector2.zero;
            bool sprint = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (inputEnabled && kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
                sprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (kb.vKey.wasPressedThisFrame) onBike = !onBike;
            }
            if (inputEnabled && mouse != null && Cursor.lockState == CursorLockMode.Locked) look = mouse.delta.ReadValue() * lookSpeed;
#else
            if (inputEnabled)
            {
                move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                sprint = Input.GetKey(KeyCode.LeftShift);
                if (Input.GetKeyDown(KeyCode.V)) onBike = !onBike;
                if (Cursor.lockState == CursorLockMode.Locked) look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2;
            }
#endif
            transform.Rotate(0, look.x, 0);
            pitch = Mathf.Clamp(pitch - look.y, -85, 85);
            if (cam != null)
            {
                cam.transform.localPosition = new Vector3(0, onBike ? 1.95f : 1.68f, 0);
                cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            float speed = onBike ? bike : sprint ? run : walk;
            var dir = transform.TransformDirection(new Vector3(move.x, 0, move.y));
            if (dir.sqrMagnitude > 1) dir.Normalize();
            vy = cc.isGrounded ? -2f : vy - 20f * Time.deltaTime;
            cc.Move((dir * speed + Vector3.up * vy) * Time.deltaTime);
            // never fall through the terrain
            if (Terrain.activeTerrain != null)
            {
                float g = Terrain.activeTerrain.SampleHeight(transform.position) + Terrain.activeTerrain.transform.position.y;
                if (transform.position.y < g - 0.5f) Place(new Vector3(transform.position.x, g + 0.1f, transform.position.z), transform.eulerAngles.y);
            }
        }
    }
}
