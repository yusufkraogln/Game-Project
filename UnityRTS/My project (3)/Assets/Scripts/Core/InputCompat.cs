using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Cross-compatible input helper supporting both Unity 6 New Input System
    /// and Legacy Input Manager without runtime InvalidOperationException.
    /// </summary>
    public static class InputCompat
    {
        public static Vector3 MousePosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null)
                {
                    Vector2 pos = Mouse.current.position.ReadValue();
                    return new Vector3(pos.x, pos.y, 0f);
                }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePosition;
#else
                return Vector3.zero;
#endif
            }
        }

        public static bool GetMouseButtonDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (button == 0) return Mouse.current.leftButton.wasPressedThisFrame;
                if (button == 1) return Mouse.current.rightButton.wasPressedThisFrame;
                if (button == 2) return Mouse.current.middleButton.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(button);
#else
            return false;
#endif
        }

        public static bool GetMouseButton(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (button == 0) return Mouse.current.leftButton.isPressed;
                if (button == 1) return Mouse.current.rightButton.isPressed;
                if (button == 2) return Mouse.current.middleButton.isPressed;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#else
            return false;
#endif
        }

        public static bool GetMouseButtonUp(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (button == 0) return Mouse.current.leftButton.wasReleasedThisFrame;
                if (button == 1) return Mouse.current.rightButton.wasReleasedThisFrame;
                if (button == 2) return Mouse.current.middleButton.wasReleasedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(button);
#else
            return false;
#endif
        }

        public static float GetScrollDeltaY()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.scroll.ReadValue().y / 120f;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#else
            return 0f;
#endif
        }

        public static Vector2 GetMovementAxes()
        {
            float x = 0f;
            float y = 0f;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1f;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1f;
                return new Vector2(x, y);
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            x = Input.GetAxisRaw("Horizontal");
            y = Input.GetAxisRaw("Vertical");
#endif
            return new Vector2(x, y);
        }

        public static bool IsShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
                return Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
            return false;
#endif
        }

        public static bool WasKeyPressedThisFrame(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                switch (keyCode)
                {
                    case KeyCode.F1: return Keyboard.current.f1Key.wasPressedThisFrame;
                    case KeyCode.Alpha1: return Keyboard.current.digit1Key.wasPressedThisFrame;
                    case KeyCode.Alpha2: return Keyboard.current.digit2Key.wasPressedThisFrame;
                    case KeyCode.Alpha3: return Keyboard.current.digit3Key.wasPressedThisFrame;
                    case KeyCode.Alpha4: return Keyboard.current.digit4Key.wasPressedThisFrame;
                    case KeyCode.Alpha5: return Keyboard.current.digit5Key.wasPressedThisFrame;
                    case KeyCode.Alpha6: return Keyboard.current.digit6Key.wasPressedThisFrame;
                    case KeyCode.Tab: return Keyboard.current.tabKey.wasPressedThisFrame;
                    case KeyCode.C: return Keyboard.current.cKey.wasPressedThisFrame;
                    case KeyCode.R: return Keyboard.current.rKey.wasPressedThisFrame;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#else
            return false;
#endif
        }

        public static bool IsKeyHeld(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                switch (keyCode)
                {
                    case KeyCode.Q: return Keyboard.current.qKey.isPressed;
                    case KeyCode.E: return Keyboard.current.eKey.isPressed;
                    case KeyCode.Space: return Keyboard.current.spaceKey.isPressed;
                    case KeyCode.LeftControl: return Keyboard.current.leftCtrlKey.isPressed;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(keyCode);
#else
            return false;
#endif
        }
    }
}
