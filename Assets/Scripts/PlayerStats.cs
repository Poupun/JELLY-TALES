using UnityEngine;
using System;

public class PlayerStats : MonoBehaviour
{
    [Header("Health Settings")]
    [Range(1, 100)]
    public int maxHealth = 20;
    
    [Header("Hunger Settings")]
    [Range(1, 100)]
    public int maxHunger = 20;
    public float hungerDecayRate = 1f; // Pesr minute
    public int healthLossOnStarvation = 1;
    public float starvationInterval = 4f; // Seconds between health loss when starving
    
    [Header("Health Regeneration")]
    public bool enableHealthRegen = true;
    public int regenThreshold = 18; // Hunger level needed for regen
    public int regenAmount = 1;
    public float regenInterval = 4f; // Seconds between regen ticks
    
    // Current stats
    private int currentHealth;
    private float currentHunger;
    
    // Timers
    private float hungerTimer;
    private float starvationTimer;
    private float regenTimer;
    
    // Events for UI updates
    public static event Action<int, int> OnHealthChanged;
    public static event Action<float, int> OnHungerChanged;
    public static event Action OnPlayerDeath;
    
    // Properties for external access
    public int CurrentHealth => currentHealth;
    public float CurrentHunger => currentHunger;
    public int MaxHealth => maxHealth;
    public int MaxHunger => maxHunger;
    public bool IsDead => currentHealth <= 0;
    public bool IsStarving => currentHunger <= 0;
    public bool CanRegenerate => currentHunger >= regenThreshold && currentHealth < maxHealth;
    
    void Start()
    {
        // Initialize stats to max
        currentHealth = maxHealth;
        currentHunger = maxHunger;
        
        // Notify UI
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
    }
    
    void Update()
    {
        if (IsDead) return;
        
        UpdateHunger();
        UpdateHealthEffects();
    }
    
    private void UpdateHunger()
    {
        // Decrease hunger over time
        hungerTimer += Time.deltaTime;
        if (hungerTimer >= 60f) // Every minute
        {
            hungerTimer = 0f;
            ModifyHunger(-hungerDecayRate);
        }
    }
    
    private void UpdateHealthEffects()
    {
        if (IsStarving)
        {
            // Lose health when starving
            starvationTimer += Time.deltaTime;
            if (starvationTimer >= starvationInterval)
            {
                starvationTimer = 0f;
                ModifyHealth(-healthLossOnStarvation);
            }
        }
        else if (enableHealthRegen && CanRegenerate)
        {
            // Regenerate health when well-fed
            regenTimer += Time.deltaTime;
            if (regenTimer >= regenInterval)
            {
                regenTimer = 0f;
                ModifyHealth(regenAmount);
            }
        }
    }
    
    public void ModifyHealth(int amount)
    {
        if (IsDead && amount <= 0) return;
        
        currentHealth = Mathf.Clamp(currentHealth + amount, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        
        if (currentHealth <= 0)
        {
            OnPlayerDeath?.Invoke();
        }
    }
    
    public void ModifyHunger(float amount)
    {
        currentHunger = Mathf.Clamp(currentHunger + amount, 0, maxHunger);
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
    }
    
    public void SetHealth(int health)
    {
        currentHealth = Mathf.Clamp(health, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        
        if (currentHealth <= 0)
        {
            OnPlayerDeath?.Invoke();
        }
    }
    
    public void SetHunger(float hunger)
    {
        currentHunger = Mathf.Clamp(hunger, 0, maxHunger);
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
    }
    
    public void RestoreHealth(int amount)
    {
        ModifyHealth(amount);
    }
    
    public void RestoreHunger(float amount)
    {
        ModifyHunger(amount);
    }
    
    public void TakeDamage(int damage)
    {
        ModifyHealth(-damage);
    }
    
    // Reset stats (for respawn or debug)
    public void ResetStats()
    {
        currentHealth = maxHealth;
        currentHunger = maxHunger;
        hungerTimer = 0f;
        starvationTimer = 0f;
        regenTimer = 0f;
        
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
    }
}