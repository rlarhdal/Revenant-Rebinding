using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DatanSave;
using DefaultNamespace.UI;
using DefaultNamespace.UI.KeyMapping;
using TMPro;
using Tools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Serialization;
using UnityEngine.UI;

public enum KeySetting
{
    None, //0
    UpMain, //1
    UpSub, //1
    UpGamePad, //1
    DownMain, //2 
    DownSub, //2 
    DownGamePad, //2 
    LeftMain, //3
    LeftSub, //3
    LeftGamePad, //3
    RightMain, //4
    RightSub, //4
    RightGamePad, //4
    RollMain, //5
    RollSub, //5
    RollGamePad, //5
    JumpMain, //6
    JumpSub, //6
    JumpGamePad, //6
    InteractionMain, //7
    InteractionSub, //7
    InteractionGamePad, //7
    FireMain, //8
    FireSub, //8
    FireGamePad, //8
    ReloadMain, //9
    ReloadSub, //9
    ReloadGamePad, //9
    Skill0Main, //10
    Skill0Sub, //10
    Skill0GamePad, //10
    Skill1Main, //11
    Skill1Sub, //11
    Skill1GamePad, //11
}

public class RebindingUI : SettingBase
{
    [Header("# InputSystems")] 
    public InputActionAsset Actions;
    
    [Header("# UIs")] 
    public RectTransform InfoBar;
    public RectTransform ChangedBar;
    public TextMeshProUGUI InfoTitle;
    public TextMeshProUGUI ChangedText;
    public float UITime;

    private bool _isChanged = false;

    public List<KeyMapping> KeyMappings;

    private InputAction _currentAction;
    private KeySetting _currentKey;

    private InputActionRebindingExtensions.RebindingOperation _rebindingOperation = null;
    private CancellationTokenSource _openChangeTokenSource = null;
    private CancellationTokenSource _openApplyTokenSource = null;

    private const string _nullText = "비어있음";
    private const string _specialText = "특수 키는 입력할 수 없습니다.";
    private const string _withCancelingThrough = "<Keyboard>/escape";

    private static readonly HashSet<Key> specialKeys = new HashSet<Key>
    {
        Key.Enter,
        Key.Escape,
        Key.Tab,
        Key.CapsLock,
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
    };

    private void OnEnable()
    {
        Init();
        
        ExtraTools.ResetTokenSource(ref _openChangeTokenSource);
        ExtraTools.ResetTokenSource(ref _openApplyTokenSource);
    }

    private void OnDisable()
    {
        ExtraTools.DisposeTokenSource(ref _openChangeTokenSource);
        ExtraTools.DisposeTokenSource(ref _openApplyTokenSource);
    }

    protected override void Init()
    {
        _isChanged = false;
        Menu = SettingMenus.Control;
        
        var rebinds = PlayerPrefs.GetString("rebinds");
        if (!string.IsNullOrEmpty(rebinds))
            Actions.LoadBindingOverridesFromJson(rebinds);
        
        // UI 초기화
        InfoBar.gameObject.SetActive(false);
        ChangedBar.gameObject.SetActive(false);
        
        LoadSaveData();
    }

    private void UpdateBindingDisplay()
    {
        foreach (var VARIABLE in KeyMappings)
        {
            string displayString = string.Empty;
            var action = VARIABLE.ActionReference.action;
            int bindingIndex = -1;

            if (action != null)
            {
                if (string.IsNullOrEmpty(VARIABLE.ButtonPath))
                {
                    // 바인딩 인덱스
                    bindingIndex = action.bindings.IndexOf(x =>
                    {
                        return x.path == "<Keyboard>/None";
                    });
                }
                else
                {
                    // 바인딩 인덱스
                    bindingIndex = action.bindings.IndexOf(x =>
                    {
                        return x.path == VARIABLE.ButtonPath;
                    });
                }
            }

            if (bindingIndex != -1)
            {
                // 디스플레이 문자열 가져오기
                displayString = action.GetBindingDisplayString(bindingIndex);
            }

            VARIABLE.ButtonText.text = string.IsNullOrEmpty(displayString) ? _nullText : displayString;
        }
    }

    private bool ResolveActionAndBinding(KeyMapping keyMapping, out InputAction action, out int bindingIndex)
    {
        bindingIndex = -1;

        action = keyMapping.ActionReference.action;
        if (action == null)
            return false;

        // BindingIndex
        if (string.IsNullOrEmpty(keyMapping.ButtonPath))
        {
            bindingIndex = action.bindings.IndexOf(x =>
            {
                return x.path == "<Keyboard>/None";
            });
        }
        else 
        {
            bindingIndex = action.bindings.IndexOf(x =>
            {
                return x.path == keyMapping.ButtonPath;
            });
        }


        if (bindingIndex == -1)
        {
            Debug.LogError($"Cannot find binding with ID '{keyMapping.ButtonPath}' on '{action}'", this);
            return false;
        }

        return true;
    }
    
    public void StartInteractiveRebind(KeyMapping keyMapping)
    {
        if (keyMapping == null)
        {
            Debug.LogError("StartInteractiveRebind 호출 시 keyMapping이 null입니다.");
            return;
        }
        
        OpenInfoBar(keyMapping); // UI
        
        if (!ResolveActionAndBinding(keyMapping, out var action, out var bindingIndex) || action == null)
        {
            Debug.LogError("ResolveActionAndBinding 실패: action 또는 bindingIndex가 null입니다.");
            return;
        }

        action.Disable();
        PerformInteractiveRebind(keyMapping, action, bindingIndex);
        SettingUIHandler.IsPopup = true;
    }

    private void PerformInteractiveRebind(KeyMapping keyMapping, InputAction action, int bindingIndex)
    {
        string displayName = String.Empty;
        _rebindingOperation?.Cancel();

        void CleanUp()
        {
            if (_rebindingOperation != null)
            {
                _rebindingOperation.Dispose();
                _rebindingOperation = null;
            }
            action.Enable();
            InfoBar.gameObject.SetActive(false);
        }

        // 리바인딩 시작
        _rebindingOperation = action.PerformInteractiveRebinding(bindingIndex)
            .OnMatchWaitForAnother(0.1f)
            .OnPotentialMatch(operation =>
            {
                var control = operation.selectedControl;
                
                // 바인딩 취소
                if (control.path.Contains("escape") || control.path.Contains("start"))
                {
                    operation.Cancel();
                    return;
                }

                if (IsSpecialGamdpadKey(control.path))
                {
                    ExtraTools.ResetTokenSource(ref _openChangeTokenSource);
                    operation.Cancel();
                    OpenChangedBar(_openChangeTokenSource.Token, keyMapping).Forget();
                    return;
                }
                
                if (control is KeyControl keyControl && IsSpecialKey(keyControl.keyCode))
                {
                    ExtraTools.ResetTokenSource(ref _openChangeTokenSource);
                    operation.Cancel();
                    OpenChangedBar(_openChangeTokenSource.Token, keyMapping).Forget();
                    return;
                }

                // 입력 경로 표준화
                var standardizedInputPath = StandardizeControlPath(control);

                // 중복 바인딩 확인 및 제거
                foreach (var otherAction in action.actionMap.actions)
                {
                    for (int i = 0; i < otherAction.bindings.Count; i++)
                    {
                        var binding = otherAction.bindings[i];
                        if (string.IsNullOrEmpty(binding.effectivePath))
                            continue;

                        // 바인딩된 경로를 표준화
                        var bindingControl = InputControlPath.TryFindControl(control.device, binding.effectivePath);
                        if (bindingControl == null) continue;

                        // Matches 함수를 사용한 중복 확인
                        if (InputControlPath.Matches(standardizedInputPath, bindingControl))
                        {
                            Debug.LogWarning(
                                $"입력 '{standardizedInputPath}'는 다른 액션 '{otherAction.name}'에 이미 바인딩되어 있습니다. 기존 바인딩을 제거합니다.");

                            // 중복된 바인딩 해제
                            otherAction.ApplyBindingOverride(i, string.Empty);
                        }
                    }
                }
            })
            .OnCancel(
                operation =>
                {
                    CleanUp();
                })
            .OnComplete(
                operation =>
                {
                    displayName = operation.selectedControl.displayName;
                    _isChanged = true;
                    ExtraTools.ResetTokenSource(ref _openChangeTokenSource);
                    UpdateBindingDisplay();
                    CleanUp();
                    OpenChangedBar(_openChangeTokenSource.Token, keyMapping, displayName).Forget();
                });

        _rebindingOperation.Start();
    }

    // 입력 경로를 표준화하는 함수
    private string StandardizeControlPath(InputControl control)
    {
        var path = control.path;

        if (path.Contains("buttonSouth")) return "<Gamepad>/buttonSouth"; // A / Cross
        if (path.Contains("buttonNorth")) return "<Gamepad>/buttonNorth"; // Y / Triangle
        if (path.Contains("buttonEast")) return "<Gamepad>/buttonEast"; // B / Circle
        if (path.Contains("buttonWest")) return "<Gamepad>/buttonWest"; // X / Square
        if (path.Contains("leftStick")) return "<Gamepad>/leftStick";
        if (path.Contains("rightStick")) return "<Gamepad>/rightStick";
        if (path.Contains("dpad")) return "<Gamepad>/dpad";
        if (path.Contains("leftTrigger")) return "<Gamepad>/leftTrigger";
        if (path.Contains("rightTrigger")) return "<Gamepad>/rightTrigger";
        if (path.Contains("start")) return "<Gamepad>/start";

        return control.path; // 다른 경로는 그대로 반환
    }

    public override void ResetToDefault()
    {
        foreach (var keyMapping in KeyMappings)
        {
            if (!ResolveActionAndBinding(keyMapping, out var action, out var bindingIndex))
                continue;
        
            action.RemoveBindingOverride(bindingIndex);
        }
        
        UpdateBindingDisplay();
        _isChanged = true;
    }
    
    private bool IsSpecialKey(Key controlKeyCode)
    {
        return specialKeys.Contains(controlKeyCode);
    }
    
    private bool IsSpecialGamdpadKey(string controlPath)
    {
        if (controlPath.Contains("leftstick") || controlPath.Contains("dPad"))
            return true;
        
        return false;
    }
    
    private void OpenInfoBar(KeyMapping keyMapping)
    {
        InfoBar.gameObject.SetActive(true);
        InfoTitle.text = $"\"{keyMapping.Title.text}\" 단축키 지정";
    }
    
    private async UniTaskVoid OpenChangedBar(CancellationToken token, KeyMapping keyMapping, string displayName = null)
    {
        ChangedBar.gameObject.SetActive(true);
        
        if (displayName != null)
        {
            ChangedText.text = $"\"{keyMapping.Title.text}\"의 단축키가 {displayName}로 변경됐습니다.";
            SettingUIHandler.SetIsChanged(true);
        }
        else
        {
            ChangedText.text = _specialText;
        }

        await UniTask.Delay(TimeSpan.FromSeconds(UITime), DelayType.Realtime, cancellationToken: token);

        ChangedBar.gameObject.SetActive(false);
        SettingUIHandler.IsPopup = false;
    }
    
    public override void SaveData()
    {
        var rebindingData = SettingUIHandler.GetSettingSaveData<RebindingSettingData>(SettingMenus.Control);
        rebindingData.SaveRebindingData(Actions);
        _isChanged = false;
    }

    public override void LoadSaveData()
    {
        var rebindingData = SettingUIHandler.GetSettingSaveData<RebindingSettingData>(SettingMenus.Control);
        rebindingData.LoadRebindingData(Actions);
        
        UpdateBindingDisplay();
    }
}