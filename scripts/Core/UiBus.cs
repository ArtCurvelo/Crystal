using System;

namespace CrystalExcavator.Core;

/// <summary>Canal desacoplado entre objetos do mundo e telas de interface.</summary>
public static class UiBus
{
    public static event Action OpenSell;
    public static event Action OpenShop;
    public static event Action<string> RegionEntered;

    public static void RequestSell() => OpenSell?.Invoke();
    public static event Action OpenHome;
    public static void RequestHome() => OpenHome?.Invoke();
    public static event Action OpenHomePanel;
    public static void RequestHomePanel() => OpenHomePanel?.Invoke();
    public static event Action Sleep;
    public static void RequestSleep() => Sleep?.Invoke();
    public static event Action GoToMine;
    public static void RequestMine() => GoToMine?.Invoke();
    public static void RequestShop() => OpenShop?.Invoke();
    public static event Action PrestigeRequested;
    public static void RequestPrestige() => PrestigeRequested?.Invoke();
    public static void EnterRegion(string id) => RegionEntered?.Invoke(id);
}
