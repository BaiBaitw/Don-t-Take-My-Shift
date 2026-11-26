using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Watches the IsRunning value of the countdown script on the TimerManager object:
/// IsRunning == true  -> play the animation on this object
/// IsRunning == false -> pause the animation (freeze on current frame)
///
/// The countdown script is found by its class name (case-insensitive, namespace ignored),
/// so this file compiles even if the class name or namespace differs slightly.
/// </summary>
[RequireComponent(typeof(Animator))]
public class FactoryTable : MonoBehaviour
{
    [Header("Drag in the TimerManager object that has the countdown script")]
    [SerializeField] private GameObject TimerManager;

    [Header("Class name of the countdown script (case-insensitive)")]
    [SerializeField] private string countdownScriptName = "countdownBar";

    [Header("Name of the bool to watch")]
    [SerializeField] private string isRunningName = "IsRunning";

    private Component countdown;
    private PropertyInfo runningProperty;
    private FieldInfo runningField;
    private Animator animator;
    private bool lastState;

    private void Awake()
    {
        animator = GetComponent<Animator>();

        // If not assigned in the Inspector, try to find it by name
        if (TimerManager == null)
            TimerManager = GameObject.Find("TimerManager");

        if (TimerManager == null)
        {
            Debug.LogWarning("[FactoryTable] TimerManager object not found!", this);
            enabled = false;
            return;
        }

        // Find the countdown component by class name
        foreach (Component c in TimerManager.GetComponents<Component>())
        {
            if (c != null && string.Equals(c.GetType().Name, countdownScriptName, StringComparison.OrdinalIgnoreCase))
            {
                countdown = c;
                break;
            }
        }

        if (countdown == null)
        {
            Debug.LogWarning("[FactoryTable] No component named '" + countdownScriptName + "' on TimerManager. " +
                             "Components found: " + ListComponents(), this);
            enabled = false;
            return;
        }

        // Find IsRunning as a property or a field (public or private)
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        Type t = countdown.GetType();
        runningProperty = t.GetProperty(isRunningName, flags);
        if (runningProperty == null || runningProperty.PropertyType != typeof(bool))
        {
            runningProperty = null;
            runningField = t.GetField(isRunningName, flags);
            if (runningField != null && runningField.FieldType != typeof(bool))
                runningField = null;
        }

        if (runningProperty == null && runningField == null)
        {
            Debug.LogWarning("[FactoryTable] '" + t.Name + "' has no bool named '" + isRunningName + "'.", this);
            enabled = false;
            return;
        }

        // Apply the initial state
        lastState = ReadIsRunning();
        ApplyState(lastState);
    }

    private void Update()
    {
        bool running = ReadIsRunning();

        // Only switch when the state changes, instead of every frame
        if (running != lastState)
        {
            lastState = running;
            ApplyState(running);
        }
    }

    private bool ReadIsRunning()
    {
        if (runningProperty != null) return (bool)runningProperty.GetValue(countdown);
        return (bool)runningField.GetValue(countdown);
    }

    private void ApplyState(bool running)
    {
        // speed = 1 plays normally; speed = 0 freezes on the current frame (paused)
        animator.speed = running ? 1f : 0f;
    }

    private string ListComponents()
    {
        string s = "";
        foreach (Component c in TimerManager.GetComponents<Component>())
            if (c != null) s += c.GetType().Name + ", ";
        return s;
    }
}