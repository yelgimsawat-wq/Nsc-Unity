using System;
using System.Linq;
using System.Reflection;
using NscUnity.Items;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class ItemAuthorityTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private HandItemHolder holder;
    private ChargeGunHeldItem gun;
    private ItemPickupInteractor pickup;

    [SetUp]
    public void SetUp()
    {
        // Inactive objects let us configure references without starting a network session.
        root = new GameObject("Item authority test");
        root.SetActive(false);
        root.AddComponent<NetworkObject>();
        holder = root.AddComponent<HandItemHolder>();
        pickup = root.AddComponent<ItemPickupInteractor>();
        var weapon = new GameObject("Charge gun");
        weapon.transform.SetParent(root.transform);
        gun = weapon.AddComponent<ChargeGunHeldItem>();
        typeof(HeldItem).GetMethod("Bind", PrivateInstance).Invoke(gun, new object[] { null, holder });
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(0f)]
    [TestCase(0.1f)]
    public void InvalidOrInsufficientChargeCannotCreateShot(float seconds)
    {
        Assert.That(TryBuildShot(seconds, out _), Is.False);
    }

    [Test]
    public void ServerChargeDeterminesDamageAndCapsOvercharging()
    {
        Assert.That(TryBuildShot(0.6f, out FireData half), Is.True);
        Assert.That(half.damage, Is.EqualTo(160f).Within(0.001f));
        Assert.That(TryBuildShot(1.2f, out FireData full), Is.True);
        Assert.That(full.damage, Is.EqualTo(260f).Within(0.001f));
        Assert.That(TryBuildShot(120f, out FireData excess), Is.True);
        Assert.That(excess.damage, Is.EqualTo(full.damage));
        Assert.That(excess.visualSize, Is.EqualTo(full.visualSize));
        Assert.That(excess.hitRadius, Is.EqualTo(full.hitRadius));
    }

    [Test]
    public void ShotUsesServerWeaponTransformAndConfiguration()
    {
        gun.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 90f, 0f));
        typeof(ChargeGunHeldItem).GetField("speed", PrivateInstance).SetValue(gun, 17f);
        Assert.That(TryBuildShot(1.2f, out FireData shot), Is.True);
        Assert.That(shot.origin, Is.EqualTo(gun.transform.position));
        Assert.That(Vector3.Distance(shot.direction, gun.transform.forward), Is.LessThan(0.001f));
        Assert.That(shot.speed, Is.EqualTo(17f));
    }

    [TestCase(0f, true)]
    [TestCase(2.5f, true)]
    [TestCase(2.51f, false)]
    [TestCase(float.NaN, false)]
    [TestCase(float.PositiveInfinity, false)]
    public void PickupRejectsPositionsOutsideServerRange(float distance, bool expected)
    {
        Assert.That(InPickupReach(new Vector3(distance, 0f, 0f)), Is.EqualTo(expected));
    }

    [Test]
    public void PickupUsesConfiguredOriginAndAngle()
    {
        var origin = new GameObject("Detection origin");
        origin.transform.SetParent(root.transform);
        origin.transform.position = new Vector3(10f, 0f, 0f);
        typeof(ItemPickupInteractor).GetField("detectionOrigin", PrivateInstance).SetValue(pickup, origin.transform);
        typeof(ItemPickupInteractor).GetField("maxPickupAngle", PrivateInstance).SetValue(pickup, 45f);
        Assert.That(InPickupReach(origin.transform.position + Vector3.forward), Is.True);
        Assert.That(InPickupReach(origin.transform.position - Vector3.forward), Is.False);
        Assert.That(InPickupReach(Vector3.forward), Is.False);
    }

    [TestCase(typeof(PlayerInventory), "EquipRpc", RpcInvokePermission.Owner)]
    [TestCase(typeof(PlayerInventory), "EquipRelativeRpc", RpcInvokePermission.Owner)]
    [TestCase(typeof(PlayerInventory), "DropRpc", RpcInvokePermission.Owner)]
    [TestCase(typeof(ItemPickupInteractor), "PickupRpc", RpcInvokePermission.Owner)]
    [TestCase(typeof(HandItemHolder), "UseStartRpc", RpcInvokePermission.Owner)]
    [TestCase(typeof(HandItemHolder), "UseCancelRpc", RpcInvokePermission.Owner)]
    [TestCase(typeof(HandItemHolder), "UseReleaseRpc", RpcInvokePermission.Owner)]
    [TestCase(typeof(HandItemHolder), "ShotRpc", RpcInvokePermission.Server)]
    public void NetworkCommandsRequireIntendedAuthority(Type type, string method, RpcInvokePermission permission)
    {
        Assert.That(type.GetMethod(method, PrivateInstance).GetCustomAttribute<RpcAttribute>().InvokePermission,
            Is.EqualTo(permission));
    }

    [Test]
    public void MenuSceneNameResolvesToEnabledGameMenu()
    {
        // SceneManager.LoadScene(name) เลือกฉากแรกในรายการ build ที่ชื่อตรง → ต้องเป็นเมนูของเกมจริง
        var first = EditorBuildSettings.scenes.First(scene =>
            scene.enabled && System.IO.Path.GetFileNameWithoutExtension(scene.path) == Nsc.Match.GameScenes.Menu);
        Assert.That(first.path, Is.EqualTo("Assets/nok/scene/Game/-MenuNOk.unity"));
    }

    private bool TryBuildShot(float seconds, out FireData shot)
    {
        object[] args = { seconds, default(FireData) };
        bool result = (bool)typeof(ChargeGunHeldItem).GetMethod("TryBuildFireData", PrivateInstance).Invoke(gun, args);
        shot = (FireData)args[1];
        return result;
    }

    private bool InPickupReach(Vector3 position) =>
        (bool)typeof(ItemPickupInteractor).GetMethod("IsWithinPickupReach", PrivateInstance).Invoke(pickup, new object[] { position });
}
