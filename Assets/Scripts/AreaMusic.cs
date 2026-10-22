using UnityEngine;

public class AreaMusic : MonoBehaviour
{
    [SerializeField] private AudioClip areaMusic;

    private void Start()
    {
        MusicManager.instance.SetMusicVolume(0.1f);
        MusicManager.instance.PlayMusic(areaMusic);
    }
}