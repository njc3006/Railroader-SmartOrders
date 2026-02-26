using SmartOrders.HarmonyPatches;

namespace SmartOrders;

using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Model;
using Model.AI;
using Track;
using UI.Common;
using UnityEngine;
using Game;
using Network.Messages;
using Model.Ops;
using static Model.Car;

public static class SmartOrdersUtility
{
    public static void ConnectAir(BaseLocomotive locomotive)
    {
        DebugLog("Checking air");
        locomotive.EnumerateCoupled().Do(car =>
        {
            ConnectAirCore(car, Car.LogicalEnd.A);
            ConnectAirCore(car, Car.LogicalEnd.B);
        });

        static void ConnectAirCore(Car car, Car.LogicalEnd end)
        {
            if (car[end].IsCoupled)
            {
                StateManager.ApplyLocal(new PropertyChange(car.id, CarPatches.KeyValueKeyFor(Car.EndGearStateKey.Anglecock, car.LogicalToEnd(end)), new FloatPropertyValue(car[end].IsCoupled ? 1f : 0f)));

                if (car.TryGetAdjacentCar(end, out var car2))
                {
                    StateManager.ApplyLocal(new SetGladhandsConnected(car.id, car2.id, true));
                }
            }
            else { car.ApplyEndGearChange(end, EndGearStateKey.Anglecock, 0f); }
        }
    }

    public static void OilTheTrain(BaseLocomotive locomotive)
    {
        DebugLog("Oiling train");
        var cars = locomotive.EnumerateCoupled();
        var numOiled = 0;

        if ((double)locomotive.VelocityMphAbs > 5.0)
        {
            Say("The brakeman can't walk that fast, slow the fuck down");
            return;
        }

        foreach (var car in cars)
        {
            if (car.Oiled < .5)
            {
                var amount = Mathf.Clamp01(.5f - car.Oiled);
                StateManager.ApplyLocal(new PropertyChange(car.id, "oiled", new FloatPropertyValue(car.Oiled + amount)));
                numOiled++;
            }
        } 

        Say(numOiled == 0 ? "No cars to oil, looks good boss" : $"Oiled {numOiled} cars boss");
    }

    public static void ReleaseAllHandbrakes(BaseLocomotive locomotive)
    {
        DebugLog("Checking handbrakes");
        locomotive.EnumerateCoupled().Do(c => c.SetHandbrake(false));
    }

        
    public static void DebugLog(string message)
    {
        if (!SmartOrdersPlugin.Settings.EnableDebug)
        {
            return;
        }

        Say(message);
    }
    private static void Say(string message)
    {
        Alert alert = new Alert(AlertStyle.Console, AlertLevel.Info, message, TimeWeather.Now.TotalSeconds);
        WindowManager.Shared.Present(alert);
    }
    private static Location StartLocation(BaseLocomotive locomotive, List<Car> coupledCarsCached, bool forward)
    {
        var logical = (int)locomotive.EndToLogical(forward ? Car.End.F : Car.End.R);
        var car = coupledCarsCached[0];
        if (logical == 0)
        {
            var locationA = car.LocationA;
            return !locationA.IsValid ? car.WheelBoundsA : locationA;
        }

        var locationB = car.LocationB;
        return (locationB.IsValid ? locationB : car.WheelBoundsB).Flipped();
    }

    public static void DisconnectCarGroups(BaseLocomotive locomotive, int numGroups, AutoEngineerPersistence persistence)
    {
        var end = numGroups > 0 ? "front" : "back";
        numGroups = Math.Abs(numGroups);

        var orders = persistence.Orders;

        List<Car> cars;

        if (end == "front")
        {
            if (orders.Forward)
            {
                cars = locomotive.EnumerateCoupled(Car.End.R).Reverse().ToList();
            }
            else
            {
                cars = locomotive.EnumerateCoupled(Car.End.F).Reverse().ToList();
            }
        }
        else
        {
            if (orders.Forward)
            {
                cars = locomotive.EnumerateCoupled(Car.End.F).Reverse().ToList();
            }
            else
            {
                cars = locomotive.EnumerateCoupled(Car.End.R).Reverse().ToList();

            }
        }

        OpsController opsController = OpsController.Shared;

        if (cars.Count < 2)
        {
            DebugLog("ERROR: not enough cars");
            return;
        }

        Car firstCar = cars[0];


        var maybeFirstCarWaybill = firstCar.GetWaybill(opsController);
        if (maybeFirstCarWaybill == null)
        {
            return;
        }

        OpsCarPosition destination = maybeFirstCarWaybill.Value.Destination;

        Car? carToDisconnect = null;

        int carsToDisconnectCount = 0;
        int groupsFound = 1;

        foreach (Car car in cars)
        {
            var maybeWaybill = car.GetWaybill(opsController);
            if (maybeWaybill == null)
            {

                DebugLog($"Car {car.DisplayName}, has no waybill, stopping search");
                break;
            }

            OpsCarPosition thisCarDestination = maybeWaybill.Value.Destination;
            if (destination.Identifier == thisCarDestination.Identifier)
            {
                DebugLog($"Car {car.DisplayName} is part of group {groupsFound}");
                carToDisconnect = car;
                carsToDisconnectCount++;
            }
            else
            {
                if (groupsFound < numGroups)
                {
                    destination = thisCarDestination;
                    carToDisconnect = car;
                    carsToDisconnectCount++;
                    groupsFound++;
                    DebugLog($"Car {car.DisplayName} is part of new group {groupsFound}");
                }
                else
                {
                    DebugLog($"{groupsFound} groups found, stopping search");
                    break;
                }
            }
        }

        if (carsToDisconnectCount == 0)
        {
            DebugLog($"No cars found to disconnect");
            return;
        }

        Car newEndCar = cars[carsToDisconnectCount];


        var groupsMaybePlural = groupsFound > 1 ? "groups of cars" : "group of cars";

        var groupsString = numGroups == 999 ? "all cars with waybills" : $"{groupsFound} {groupsMaybePlural}";

        var carsMaybePlural = carsToDisconnectCount > 1 ? "cars" : "car";
        Say($"Disconnecting {groupsString} totalling {carsToDisconnectCount} {carsMaybePlural} from the {end} of the train");
        DebugLog($"Disconnecting coupler between {newEndCar.DisplayName} and {carToDisconnect!.DisplayName}");

        var newEndCarEndToDisconnect = (newEndCar.CoupledTo(LogicalEnd.A) == carToDisconnect) ? LogicalEnd.A : LogicalEnd.B;
        var carToDisconnectEndToDisconnect = (carToDisconnect.CoupledTo(LogicalEnd.A) == newEndCar) ? LogicalEnd.A : LogicalEnd.B;
        carToDisconnect.ApplyEndGearChange(carToDisconnectEndToDisconnect, EndGearStateKey.Anglecock, 1f);
        newEndCar.ApplyEndGearChange(newEndCarEndToDisconnect, EndGearStateKey.Anglecock, 0f);
        if (carToDisconnect.VelocityMphAbs > 0)
        { carToDisconnect.ApplyEndGearChange(carToDisconnectEndToDisconnect, EndGearStateKey.Anglecock, 0f); }
        newEndCar.ApplyEndGearChange(newEndCarEndToDisconnect, EndGearStateKey.CutLever, 1f);
    }
}