using UnityEngine;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    public Button startButton;
    public Button highScoreButton;
    public Button settingsButton;
    public Button quitButton;

    AudioSource _audio;
    AudioClip _click;

    void Start()
    {
        _audio = GetComponent<AudioSource>();
        _click = CreateClickClip();

        startButton?.onClick.AddListener(OnStart);
        highScoreButton?.onClick.AddListener(OnHighScore);
        settingsButton?.onClick.AddListener(OnSettings);
        quitButton?.onClick.AddListener(OnQuit);
    }

    static AudioClip CreateClickClip()
    {
        int rate = 44100;
        int n = rate / 20;
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            d[i] = Mathf.Exp(-t * 25f) * Mathf.Sin(2f * Mathf.PI * 900f * i / rate);
        }
        var clip = AudioClip.Create("Click", n, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }

    void PlayClick()
    {
        if (_audio != null && _click != null)
            _audio.PlayOneShot(_click);
    }

    void OnStart()     { PlayClick(); Debug.Log("MainMenu: Start"); }
    void OnHighScore() { PlayClick(); Debug.Log("MainMenu: High Scores"); }
    void OnSettings()  { PlayClick(); Debug.Log("MainMenu: Settings"); }
    void OnQuit()      { PlayClick(); Application.Quit(); }
}
