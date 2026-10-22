
using UnityEngine;

public class CharacterStats : MonoBehaviour
{
    [Header("Character Info")]
    public string characterName = "Knight";

    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;

    [Header("Mana")]
    public float maxMana = 100f;
    public float currentMana = 100f;

    [Header("Ammo")]
    public int maxAmmo = 30;
    public int currentAmmo = 30;

    [Header("HUD")]
    public CharacterHUD hud;

    void Start()
    {
        currentHealth = Mathf.Clamp(
            currentHealth, 0f, maxHealth);

        currentMana = Mathf.Clamp(
            currentMana, 0f, maxMana);

        currentAmmo = Mathf.Clamp(
            currentAmmo, 0, maxAmmo);

        if (hud != null)
        {
            hud.SetName(characterName);
            UpdateHUD();
        }
    }

    void Update()
    {
        // HEALTH CONTROLS
        if (Input.GetKeyDown(KeyCode.H))
            TakeDamage(10f);

        if (Input.GetKeyDown(KeyCode.J))
            Heal(10f);

        // MANA CONTROLS
        if (Input.GetKeyDown(KeyCode.M))
            SpendMana(10f);

        if (Input.GetKeyDown(KeyCode.N))
            RestoreMana(10f);

        // AMMO CONTROLS
        if (Input.GetKeyDown(KeyCode.K))
            UseAmmo(1);

        if (Input.GetKeyDown(KeyCode.L))
            Reload();
    }

    public void TakeDamage(float amount)
    {
        currentHealth = Mathf.Clamp(
            currentHealth - Mathf.Max(0f, amount),
            0f, maxHealth);

        UpdateHUD();
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Clamp(
            currentHealth + Mathf.Max(0f, amount),
            0f, maxHealth);

        UpdateHUD();
    }

    public void SpendMana(float amount)
    {
        currentMana = Mathf.Clamp(
            currentMana - Mathf.Max(0f, amount),
            0f, maxMana);

        UpdateHUD();
    }

    public void RestoreMana(float amount)
    {
        currentMana = Mathf.Clamp(
            currentMana + Mathf.Max(0f, amount),
            0f, maxMana);

        UpdateHUD();
    }

    public void UseAmmo(int amount)
    {
        currentAmmo = Mathf.Clamp(
            currentAmmo - Mathf.Max(0, amount),
            0, maxAmmo);

        UpdateHUD();
    }

    public void Reload()
    {
        currentAmmo = maxAmmo;
        UpdateHUD();
    }

    public void UpdateHUD()
    {
        if (hud != null)
        {
            hud.SetStats(
                currentHealth,
                maxHealth,
                currentMana,
                maxMana
            );

            hud.SetAmmo(currentAmmo, maxAmmo);
        }
    }
}



