using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using GuildIdle.UI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CombatWindowView : UIWindow,
    IUIOpenArgsReceiver<CombatWindowOpenArgs>,
    IUIStateView<CombatWindowState>
{
    [SerializeField] private Button _closeButton;
    [SerializeField] private Button _retreatButton;
    [SerializeField] private TMP_Text _closeButtonText;
    [SerializeField] private TMP_Text _retreatButtonText;
    [SerializeField] private TMP_Text _lootLabelText;
    [SerializeField] private TMP_Text _queueText;
    [SerializeField] private TMP_Text _lootCountText;
    [SerializeField] private TMP_Text _battleMarkText;
    [SerializeField] private TMP_Text _battleStatusText;
    [SerializeField] private CombatActorBinding _hero;
    [SerializeField] private CombatActorBinding _enemy;
    [SerializeField] private CombatActionBinding[] _actions = Array.Empty<CombatActionBinding>();
    [SerializeField] private CombatEventPopupBinding[] _heroEvents = Array.Empty<CombatEventPopupBinding>();
    [SerializeField] private CombatEventPopupBinding[] _enemyEvents = Array.Empty<CombatEventPopupBinding>();

    [Header("Event animation")]
    [SerializeField, Min(0.1f)] private float _eventLifetimeSeconds = 1.25f;
    [SerializeField, Min(1f)] private float _eventTravelDistance = 150f;
    [SerializeField, Min(0f)] private float _eventStackSpacing = 42f;
    [SerializeField, Range(0f, 1f)] private float _eventFadeStart = 0.55f;

    private CombatWindowOpenArgs _args;
    private int _heroEventIndex;
    private int _enemyEventIndex;
    private int[] _heroEventGenerations = Array.Empty<int>();
    private int[] _enemyEventGenerations = Array.Empty<int>();

    public void ApplyOpenArgs(CombatWindowOpenArgs args)
    {
        _args = args ?? throw new ArgumentNullException(nameof(args));
    }

    protected override void OnBind()
    {
        Bind(_closeButton, HandleClose);
        Bind(_retreatButton, HandleRetreat);
        _heroEventGenerations = new int[_heroEvents?.Length ?? 0];
        _enemyEventGenerations = new int[_enemyEvents?.Length ?? 0];
        CaptureEventOrigins(_heroEvents);
        CaptureEventOrigins(_enemyEvents);
        HideEvents(_heroEvents);
        HideEvents(_enemyEvents);
    }

    protected override void OnHide()
    {
        StopAllCoroutines();
        var closed = _args?.Closed;
        _args = null;
        closed?.Invoke();
    }

    public void Render(CombatWindowState state)
    {
        state ??= CombatWindowState.Empty;
        SetText(_queueText, state.EnemyCount > 0
            ? CombatUiText.Format(CombatUiText.EnemyQueue,
                state.EnemyIndex.ToString(CultureInfo.InvariantCulture),
                state.EnemyCount.ToString(CultureInfo.InvariantCulture))
            : CombatUiText.Get(CombatUiText.EnemyQueueEmpty));
        SetText(_closeButtonText, CombatUiText.Get(CombatUiText.Close));
        SetText(_retreatButtonText, CombatUiText.Get(CombatUiText.Retreat));
        SetText(_lootLabelText, CombatUiText.Get(CombatUiText.Loot));
        SetText(_lootCountText, state.LootQuantity.ToString(CultureInfo.InvariantCulture));
        SetText(_battleMarkText, state.StatusMark);
        SetText(_battleStatusText, state.StatusText);
        if (_retreatButton != null) _retreatButton.interactable = state.CanRetreat;
        _hero?.Render(state.Hero);
        _enemy?.Render(state.Enemy);

        var actions = state.Actions ?? Array.Empty<CombatActionState>();
        for (var index = 0; index < (_actions?.Length ?? 0); index++)
            _actions[index]?.Render(index < actions.Count ? actions[index] : null);
    }

    public void ShowEvents(IReadOnlyList<CombatEventPopupState> events)
    {
        if (events == null)
            return;
        foreach (var state in events)
        {
            if (state == null)
                continue;
            if (state.Target == CombatEventTarget.Hero)
                ShowEvent(_heroEvents, _heroEventGenerations, ref _heroEventIndex, state);
            else
                ShowEvent(_enemyEvents, _enemyEventGenerations, ref _enemyEventIndex, state);
        }
    }

    private void ShowEvent(
        CombatEventPopupBinding[] bindings,
        int[] generations,
        ref int cursor,
        CombatEventPopupState state)
    {
        if (bindings == null || bindings.Length == 0)
            return;
        ShiftVisibleEventsUp(bindings, _eventStackSpacing);
        var index = cursor++ % bindings.Length;
        var binding = bindings[index];
        if (binding == null)
            return;
        var generation = ++generations[index];
        binding.Render(state);
        StartCoroutine(AnimateEvent(binding, generations, index, generation));
    }

    private IEnumerator AnimateEvent(
        CombatEventPopupBinding binding,
        int[] generations,
        int index,
        int generation)
    {
        var lifetime = Mathf.Max(0.1f, _eventLifetimeSeconds);
        var elapsed = 0f;
        while (elapsed < lifetime && index < generations.Length && generations[index] == generation)
        {
            var delta = Mathf.Min(Time.unscaledDeltaTime, lifetime - elapsed);
            elapsed += delta;
            var progress = Mathf.Clamp01(elapsed / lifetime);
            var fadeProgress = _eventFadeStart >= 1f
                ? 0f
                : Mathf.InverseLerp(_eventFadeStart, 1f, progress);
            binding.MoveUp(_eventTravelDistance / lifetime * delta);
            binding.SetAlpha(1f - fadeProgress);
            yield return null;
        }
        if (index < generations.Length && generations[index] == generation)
            binding.Hide();
    }

    private static void ShiftVisibleEventsUp(CombatEventPopupBinding[] bindings, float distance)
    {
        if (distance <= 0f)
            return;
        foreach (var binding in bindings ?? Array.Empty<CombatEventPopupBinding>())
        {
            if (binding?.IsVisible == true)
                binding.MoveUp(distance);
        }
    }

    private static void CaptureEventOrigins(CombatEventPopupBinding[] bindings)
    {
        foreach (var binding in bindings ?? Array.Empty<CombatEventPopupBinding>())
            binding?.CaptureOrigin();
    }

    private void HandleClose() => _args?.Close?.Invoke();
    private void HandleRetreat() => _args?.Retreat?.Invoke();
    private void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;
        button.onClick.AddListener(action);
        RegisterCleanup(() => button.onClick.RemoveListener(action));
    }

    private static void HideEvents(CombatEventPopupBinding[] bindings)
    {
        foreach (var binding in bindings ?? Array.Empty<CombatEventPopupBinding>())
            binding?.Hide();
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }
}

[Serializable]
public sealed class CombatActorBinding
{
    public TMP_Text Name;
    public Image Portrait;
    public TMP_Text PortraitFallback;
    public Image HealthFill;
    public TMP_Text HealthValue;
    public TMP_Text ArmorValue;
    public TMP_Text DodgeValue;
    public GameObject ConsumableRoot;
    public Image ConsumableIcon;
    public TMP_Text ConsumableName;
    public TMP_Text ConsumableEffect;
    public TMP_Text ConsumableCharges;

    public void Render(CombatActorState state)
    {
        state ??= CombatActorState.Empty;
        SetText(Name, state.Name);
        SetText(PortraitFallback, Initial(state.Name));
        if (Portrait != null)
        {
            Portrait.sprite = state.Portrait;
            Portrait.enabled = state.Portrait != null;
            Portrait.preserveAspect = true;
        }
        if (PortraitFallback != null)
            PortraitFallback.gameObject.SetActive(state.Portrait == null);
        if (HealthFill != null)
            HealthFill.fillAmount = state.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)state.CurrentHp / state.MaxHp);
        SetText(HealthValue, $"{state.CurrentHp.ToString(CultureInfo.InvariantCulture)} / {state.MaxHp.ToString(CultureInfo.InvariantCulture)}");
        SetText(ArmorValue, CombatUiText.Format(CombatUiText.Armor,
            Math.Round(state.ArmorPercent).ToString(CultureInfo.InvariantCulture)));
        SetText(DodgeValue, CombatUiText.Format(CombatUiText.Dodge,
            Math.Round(state.DodgePercent).ToString(CultureInfo.InvariantCulture)));

        var showConsumable = state.Consumable != null && state.Consumable.Charges > 0;
        if (ConsumableRoot != null) ConsumableRoot.SetActive(showConsumable);
        if (!showConsumable) return;
        SetText(ConsumableName, state.Consumable.Name);
        SetText(ConsumableEffect, state.Consumable.Effect);
        SetText(ConsumableCharges, $"×{state.Consumable.Charges.ToString(CultureInfo.InvariantCulture)}");
        if (ConsumableIcon != null)
        {
            ConsumableIcon.sprite = state.Consumable.Icon;
            ConsumableIcon.enabled = state.Consumable.Icon != null;
        }
    }

    private static string Initial(string value) => string.IsNullOrWhiteSpace(value)
        ? "?"
        : value.Trim().Substring(0, 1).ToUpperInvariant();

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null) target.text = value ?? string.Empty;
    }
}

[Serializable]
public sealed class CombatActionBinding
{
    public GameObject Root;
    public Image Icon;
    public Image CooldownFill;
    public TMP_Text Name;
    public TMP_Text Value;
    public TMP_Text Cooldown;

    public void Render(CombatActionState state)
    {
        if (Root != null) Root.SetActive(state != null);
        if (state == null) return;
        if (Icon != null)
        {
            Icon.sprite = state.Icon;
            Icon.enabled = state.Icon != null;
        }
        if (CooldownFill != null)
        {
            CooldownFill.fillAmount = Mathf.Clamp01(state.CooldownProgress);
            CooldownFill.gameObject.SetActive(state.CooldownProgress > 0f);
        }
        if (Name != null) Name.text = state.Name ?? string.Empty;
        if (Value != null) Value.text = state.Value ?? string.Empty;
        if (Cooldown != null) Cooldown.text = state.CooldownSeconds > 0.05f
            ? CombatUiText.Format(CombatUiText.SecondsShort,
                state.CooldownSeconds.ToString("0.0", CultureInfo.InvariantCulture))
            : string.Empty;
    }
}

[Serializable]
public sealed class CombatEventPopupBinding
{
    public GameObject Root;
    public Image Icon;
    public TMP_Text Value;
    public Graphic Accent;

    private RectTransform _rectTransform;
    private Vector2 _origin;

    public bool IsVisible => Root != null && Root.activeSelf;

    public void CaptureOrigin()
    {
        _rectTransform = Root != null ? Root.transform as RectTransform : null;
        if (_rectTransform != null)
            _origin = _rectTransform.anchoredPosition;
    }

    public void Render(CombatEventPopupState state)
    {
        if (_rectTransform == null)
            CaptureOrigin();
        if (_rectTransform != null)
            _rectTransform.anchoredPosition = _origin;
        if (Root != null) Root.SetActive(true);
        if (Icon != null)
        {
            Icon.sprite = state.Icon;
            Icon.enabled = state.Icon != null;
            Icon.color = state.Color;
        }
        if (Value != null)
        {
            Value.text = state.Text ?? string.Empty;
            Value.color = state.Color;
        }
        SetAlpha(1f);
    }

    public void MoveUp(float distance)
    {
        if (_rectTransform != null && distance > 0f)
            _rectTransform.anchoredPosition += Vector2.up * distance;
    }

    public void SetAlpha(float alpha)
    {
        SetGraphicAlpha(Accent, alpha);
        SetGraphicAlpha(Icon, alpha);
        SetGraphicAlpha(Value, alpha);
    }

    public void Hide()
    {
        if (Root != null) Root.SetActive(false);
        if (_rectTransform != null)
            _rectTransform.anchoredPosition = _origin;
        SetAlpha(1f);
    }

    private static void SetGraphicAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null)
            return;
        var color = graphic.color;
        color.a = Mathf.Clamp01(alpha);
        graphic.color = color;
    }
}

public sealed class CombatWindowOpenArgs : IUIOpenArgs
{
    public CombatWindowOpenArgs(Action close, Action retreat, Action closed)
    {
        Close = close;
        Retreat = retreat;
        Closed = closed;
    }

    public Action Close { get; }
    public Action Retreat { get; }
    public Action Closed { get; }
}

public sealed class CombatWindowState : IUIState
{
    public static readonly CombatWindowState Empty = new CombatWindowState(
        0, 0, CombatActorState.Empty, CombatActorState.Empty,
        Array.Empty<CombatActionState>(), 0, 0, string.Empty, false, false);

    public CombatWindowState(
        int enemyIndex,
        int enemyCount,
        CombatActorState hero,
        CombatActorState enemy,
        IReadOnlyList<CombatActionState> actions,
        long lootQuantity,
        long combatExp,
        string outcome,
        bool canRetreat,
        bool canOpenLoot)
    {
        EnemyIndex = enemyIndex;
        EnemyCount = enemyCount;
        Hero = hero ?? CombatActorState.Empty;
        Enemy = enemy ?? CombatActorState.Empty;
        Actions = actions ?? Array.Empty<CombatActionState>();
        LootQuantity = Math.Max(0L, lootQuantity);
        CombatExp = Math.Max(0L, combatExp);
        Outcome = outcome ?? string.Empty;
        CanRetreat = canRetreat;
        CanOpenLoot = canOpenLoot;
    }

    public int EnemyIndex { get; }
    public int EnemyCount { get; }
    public CombatActorState Hero { get; }
    public CombatActorState Enemy { get; }
    public IReadOnlyList<CombatActionState> Actions { get; }
    public long LootQuantity { get; }
    public long CombatExp { get; }
    public string Outcome { get; }
    public bool CanRetreat { get; }
    public bool CanOpenLoot { get; }

    public string StatusMark => CanRetreat ? "⚔" :
        string.Equals(Outcome, "Victory", StringComparison.Ordinal) ? "✓" :
        string.Equals(Outcome, "Defeat", StringComparison.Ordinal) ? "☠" : "⚑";

    public string StatusText
    {
        get
        {
            if (CanRetreat)
                return CombatUiText.Get(CombatUiText.StatusRunning);
            var title = string.Equals(Outcome, "Victory", StringComparison.Ordinal)
                ? CombatUiText.Get(CombatUiText.OutcomeVictory)
                : string.Equals(Outcome, "Defeat", StringComparison.Ordinal)
                    ? CombatUiText.Get(CombatUiText.OutcomeDefeat)
                    : CombatUiText.Get(CombatUiText.OutcomeRetreat);
            if (!CanOpenLoot)
                return CombatUiText.Format(CombatUiText.StatusResolved, title);
            return CombatUiText.Format(CombatUiText.StatusResultPending, title,
                LootQuantity.ToString(CultureInfo.InvariantCulture),
                CombatExp.ToString(CultureInfo.InvariantCulture));
        }
    }
}

public sealed class CombatActorState
{
    public static readonly CombatActorState Empty = new CombatActorState(
        string.Empty, null, 0, 0, 0d, 0d, null);

    public CombatActorState(
        string name,
        Sprite portrait,
        int currentHp,
        int maxHp,
        double armorPercent,
        double dodgePercent,
        CombatConsumableState consumable)
    {
        Name = name ?? string.Empty;
        Portrait = portrait;
        CurrentHp = Math.Max(0, currentHp);
        MaxHp = Math.Max(0, maxHp);
        ArmorPercent = armorPercent;
        DodgePercent = dodgePercent;
        Consumable = consumable;
    }

    public string Name { get; }
    public Sprite Portrait { get; }
    public int CurrentHp { get; }
    public int MaxHp { get; }
    public double ArmorPercent { get; }
    public double DodgePercent { get; }
    public CombatConsumableState Consumable { get; }
}

public sealed class CombatConsumableState
{
    public CombatConsumableState(string name, string effect, Sprite icon, int charges)
    {
        Name = name ?? string.Empty;
        Effect = effect ?? string.Empty;
        Icon = icon;
        Charges = Math.Max(0, charges);
    }

    public string Name { get; }
    public string Effect { get; }
    public Sprite Icon { get; }
    public int Charges { get; }
}

public sealed class CombatActionState
{
    public CombatActionState(string name, string value, Sprite icon, float cooldownProgress, float cooldownSeconds)
    {
        Name = name ?? string.Empty;
        Value = value ?? string.Empty;
        Icon = icon;
        CooldownProgress = Mathf.Clamp01(cooldownProgress);
        CooldownSeconds = Mathf.Max(0f, cooldownSeconds);
    }

    public string Name { get; }
    public string Value { get; }
    public Sprite Icon { get; }
    public float CooldownProgress { get; }
    public float CooldownSeconds { get; }
}

public enum CombatEventTarget
{
    Hero,
    Enemy
}

public sealed class CombatEventPopupState
{
    public CombatEventPopupState(CombatEventTarget target, string text, Sprite icon, Color color)
    {
        Target = target;
        Text = text ?? string.Empty;
        Icon = icon;
        Color = color;
    }

    public CombatEventTarget Target { get; }
    public string Text { get; }
    public Sprite Icon { get; }
    public Color Color { get; }
}
