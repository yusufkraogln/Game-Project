using PoliticalMayhem.Core;
using UnityEngine;

namespace PoliticalMayhem.UI
{
    /// <summary>
    /// RTS & Free-Fly Camera supporting zoom-to-cursor, camera-relative WASD panning,
    /// middle-mouse / Q-E orbit rotation, 1-6 city focus, and a full Free Camera mode (C).
    /// </summary>
    public class RtsCameraController : MonoBehaviour
    {
        [Header("Movement & Zoom")]
        [SerializeField] private float _panSpeed = 30f;
        [SerializeField] private float _freeFlySpeed = 28f;
        [SerializeField] private float _zoomSpeed = 12f;
        [SerializeField] private float _minHeight = 6f;
        [SerializeField] private float _maxHeight = 105f;
        [SerializeField] private float _orbitSpeed = 0.28f;
        [SerializeField] private Vector2 _xBounds = new Vector2(-105f, 105f);
        [SerializeField] private Vector2 _zBounds = new Vector2(-115f, 95f);

        private bool _freeCameraMode;
        private float _yaw;
        private float _pitch = 52f;
        private Vector3 _lastMousePos;
        private bool _hasLastMouse;
        private Camera _cam;

        public bool IsFreeCameraMode => _freeCameraMode;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        private void Start()
        {
            // Ensure expanded bounds apply even if serialized in an older scene file
            if (_maxHeight < 105f) _maxHeight = 105f;
            if (_xBounds.y < 105f) _xBounds = new Vector2(-105f, 105f);
            if (_zBounds.x > -115f || _zBounds.y < 95f) _zBounds = new Vector2(-115f, 95f);

            if (transform.position.y < _minHeight ||
                (Mathf.Abs(transform.position.x) < 0.1f && transform.position.y < 52f && transform.position.z >= -35f))
            {
                transform.position = new Vector3(0f, 60f, -38f);
                transform.rotation = Quaternion.Euler(54f, 0f, 0f);
            }

            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _lastMousePos = InputCompat.MousePosition;
            _hasLastMouse = true;
        }

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;

            Vector3 currentMouse = InputCompat.MousePosition;
            Vector3 mouseDelta = _hasLastMouse ? (currentMouse - _lastMousePos) : Vector3.zero;
            _lastMousePos = currentMouse;
            _hasLastMouse = true;

            HandleModeAndResetHotkeys();
            HandleMouseOrbitAndLook(mouseDelta);
            HandlePanning();
            HandleZoom();
            HandleCityHotkeys();
        }

        public void ToggleFreeCameraMode()
        {
            SetFreeCameraMode(!_freeCameraMode);
        }

        public void SetFreeCameraMode(bool enabled)
        {
            _freeCameraMode = enabled;
            if (!_freeCameraMode)
            {
                // Return smoothly to clean RTS pitch & safe height bounds
                _pitch = Mathf.Clamp(_pitch, 25f, 78f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                Vector3 pos = transform.position;
                pos.y = Mathf.Clamp(pos.y, _minHeight, _maxHeight);
                pos.x = Mathf.Clamp(pos.x, _xBounds.x, _xBounds.y);
                pos.z = Mathf.Clamp(pos.z, _zBounds.x, _zBounds.y);
                transform.position = pos;
            }
        }

        public void ResetCameraView()
        {
            _freeCameraMode = false;
            _yaw = 0f;
            _pitch = 54f;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = new Vector3(0f, 60f, -38f);
        }

        private void HandleModeAndResetHotkeys()
        {
            if (GUIUtility.keyboardControl != 0) return;

            if (InputCompat.WasKeyPressedThisFrame(KeyCode.C))
            {
                ToggleFreeCameraMode();
            }
            else if (InputCompat.WasKeyPressedThisFrame(KeyCode.R))
            {
                ResetCameraView();
            }
        }

        private void HandleMouseOrbitAndLook(Vector3 mouseDelta)
        {
            bool freeLookDrag = _freeCameraMode && (InputCompat.GetMouseButton(1) || InputCompat.GetMouseButton(2));
            bool rtsOrbitDrag = !_freeCameraMode && InputCompat.GetMouseButton(2);

            if (freeLookDrag || rtsOrbitDrag)
            {
                _yaw += mouseDelta.x * _orbitSpeed;
                _pitch -= mouseDelta.y * _orbitSpeed;
                _pitch = _freeCameraMode ? Mathf.Clamp(_pitch, -80f, 85f) : Mathf.Clamp(_pitch, 20f, 80f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            if (!_freeCameraMode && GUIUtility.keyboardControl == 0)
            {
                float yawStep = 0f;
                if (InputCompat.IsKeyHeld(KeyCode.Q)) yawStep -= 65f * Time.deltaTime;
                if (InputCompat.IsKeyHeld(KeyCode.E)) yawStep += 65f * Time.deltaTime;
                if (Mathf.Abs(yawStep) > 0.001f)
                {
                    _yaw += yawStep;
                    transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                }
            }
        }

        private void HandlePanning()
        {
            if (GUIUtility.keyboardControl != 0) return;

            Vector2 move = InputCompat.GetMovementAxes();
            float speedMul = InputCompat.IsShiftHeld() ? 1.85f : 1f;

            if (_freeCameraMode)
            {
                Vector3 dir = (transform.forward * move.y) + (transform.right * move.x);
                if (InputCompat.IsKeyHeld(KeyCode.E) || InputCompat.IsKeyHeld(KeyCode.Space)) dir += Vector3.up;
                if (InputCompat.IsKeyHeld(KeyCode.Q) || InputCompat.IsKeyHeld(KeyCode.LeftControl)) dir -= Vector3.up;

                if (dir.sqrMagnitude <= 0.0001f) return;
                Vector3 nextPos = transform.position + dir.normalized * (_freeFlySpeed * speedMul * Time.deltaTime);
                nextPos.x = Mathf.Clamp(nextPos.x, _xBounds.x, _xBounds.y);
                nextPos.y = Mathf.Clamp(nextPos.y, 2.5f, _maxHeight);
                nextPos.z = Mathf.Clamp(nextPos.z, _zBounds.x, _zBounds.y);
                transform.position = nextPos;
                return;
            }

            if (move.sqrMagnitude <= 0.0001f) return;

            // Pan along camera's horizontal forward/right so W is always screen-up even when rotated
            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 flatRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            if (flatRight.sqrMagnitude < 0.001f) flatRight = Vector3.right;

            Vector3 delta = (flatForward * move.y + flatRight * move.x) * (_panSpeed * speedMul * Time.deltaTime);
            Vector3 pos = transform.position + delta;
            pos.x = Mathf.Clamp(pos.x, _xBounds.x, _xBounds.y);
            pos.z = Mathf.Clamp(pos.z, _zBounds.x, _zBounds.y);
            transform.position = pos;
        }

        private void HandleZoom()
        {
            float scroll = InputCompat.GetScrollDeltaY();
            if (Mathf.Abs(scroll) <= 0.001f) return;

            if (_freeCameraMode)
            {
                Vector3 freePos = transform.position + transform.forward * (scroll * _zoomSpeed);
                freePos.x = Mathf.Clamp(freePos.x, _xBounds.x, _xBounds.y);
                freePos.y = Mathf.Clamp(freePos.y, 2.5f, _maxHeight);
                freePos.z = Mathf.Clamp(freePos.z, _zBounds.x, _zBounds.y);
                transform.position = freePos;
                return;
            }

            // Zoom toward the cursor position on the ground plane so target cities stay centered under the pointer
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            bool hasCursorHit = false;
            Vector3 beforeHit = Vector3.zero;
            if (_cam != null)
            {
                Ray beforeRay = _cam.ScreenPointToRay(InputCompat.MousePosition);
                if (groundPlane.Raycast(beforeRay, out float enterBefore))
                {
                    beforeHit = beforeRay.GetPoint(enterBefore);
                    hasCursorHit = true;
                }
            }

            Vector3 step = transform.forward * (scroll * _zoomSpeed);
            Vector3 pos = transform.position + step;
            pos.y = Mathf.Clamp(pos.y, _minHeight, _maxHeight);
            transform.position = pos;

            if (hasCursorHit && _cam != null)
            {
                Ray afterRay = _cam.ScreenPointToRay(InputCompat.MousePosition);
                if (groundPlane.Raycast(afterRay, out float enterAfter))
                {
                    Vector3 afterHit = afterRay.GetPoint(enterAfter);
                    Vector3 correction = beforeHit - afterHit;
                    pos.x = Mathf.Clamp(pos.x + correction.x, _xBounds.x, _xBounds.y);
                    pos.z = Mathf.Clamp(pos.z + correction.z, _zBounds.x, _zBounds.y);
                    transform.position = pos;
                }
            }
        }

        private void HandleCityHotkeys()
        {
            // Quick city focus hotkeys 1..6 (only when not typing into a GUI text field)
            if (GUIUtility.keyboardControl != 0) return;

            if (InputCompat.WasKeyPressedThisFrame(KeyCode.Alpha1)) FocusCity(CityId.Baskent);
            else if (InputCompat.WasKeyPressedThisFrame(KeyCode.Alpha2)) FocusCity(CityId.Limankent);
            else if (InputCompat.WasKeyPressedThisFrame(KeyCode.Alpha3)) FocusCity(CityId.Demirkent);
            else if (InputCompat.WasKeyPressedThisFrame(KeyCode.Alpha4)) FocusCity(CityId.Yesilova);
            else if (InputCompat.WasKeyPressedThisFrame(KeyCode.Alpha5)) FocusCity(CityId.Universitekent);
            else if (InputCompat.WasKeyPressedThisFrame(KeyCode.Alpha6)) FocusCity(CityId.Dagsehir);
        }

        public void FocusCity(CityId cityId)
        {
            FocusCity(CityCatalog.GetWorldPosition(cityId));
        }

        public void FocusCity(Vector3 cityWorldPos)
        {
            if (_freeCameraMode) SetFreeCameraMode(false);

            float pitchDeg = Mathf.Clamp(_pitch, 15f, 85f);
            float pitchRad = Mathf.Deg2Rad * pitchDeg;
            float zDist = transform.position.y / Mathf.Tan(pitchRad);

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;

            Vector3 targetPos = cityWorldPos - flatForward * zDist;
            transform.position = new Vector3(
                Mathf.Clamp(targetPos.x, _xBounds.x, _xBounds.y),
                Mathf.Clamp(transform.position.y, _minHeight, _maxHeight),
                Mathf.Clamp(targetPos.z, _zBounds.x, _zBounds.y));
        }
    }
}
