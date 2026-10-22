
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterHUD : MonoBehaviour
{
    [Header("Character UI")]
    public TMP_Text characterName;
    public Slider healthBar;
    public Slider manaBar;
    public TMP_Text ammoText;

    public void SetName(string newName)
    {
        characterName.text = newName;
    }

    public void SetStats(
        float health,
        float maxHealth,
        float mana,
        float maxMana)
    {
        healthBar.value = maxHealth > 0
            ? Mathf.Clamp01(health / maxHealth)
            : 0f;

        manaBar.value = maxMana > 0
            ? Mathf.Clamp01(mana / maxMana)
            : 0f;
    }

    public void SetAmmo(int ammo, int maxAmmo)
    {
        if (ammoText != null)
        {
            ammoText.text = ammo + "/" + maxAmmo;
        }
    }
}

