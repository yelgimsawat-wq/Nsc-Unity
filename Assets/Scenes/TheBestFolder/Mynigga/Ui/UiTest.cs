using UnityEngine;
using TMPro;
public class UiTest : MonoBehaviour
{
    public TextMeshProUGUI tex;
    public void OnHelthchanged(int currenthp)
    {
        tex.text = "Health Changed! Current HP: " + currenthp;
    }
    void Start()
    {
        tex.text = "Health Changed! Current HP: " + 100;
    }
    void Update()
    {

    }
}
