using Unity.Netcode;
using UnityEngine;

public class UiSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject uiPrefab; // Drag your UI Prefab here
    private GameObject myLocalUI;

    public override void OnNetworkSpawn()
    {
        // Only the person controlling this player should spawn the UI
        if (IsOwner)
        {
            // Spawn the UI locally
            myLocalUI = Instantiate(uiPrefab);

            // Link the UI to this player's data
            SetupUI(myLocalUI);
        }
    }

    private void SetupUI(GameObject ui)
    {
        // Example: Tell the UI which player to track
        // ui.GetComponent<MyHealthBar>().SetTarget(this.GetComponent<PlayerHealth>());
        ui.SetActive(true);
        EZMovement movement = gameObject.GetComponent<EZMovement>();
        ui.GetComponent<UiTest>().OnHelthchanged(movement.hp.Value);
        movement.hp.OnValueChanged += (oldValue, newValue) =>
        {
            ui.GetComponent<UiTest>().OnHelthchanged(newValue);
        };
    }

    public override void OnNetworkDespawn()
    {
        // Destroy the UI when the player leaves or dies
        if (myLocalUI != null)
        {
            Destroy(myLocalUI);
        }
    }
}