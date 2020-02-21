﻿using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Collections;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassicUO.Game.Managers
{
    static class BoatMovingManager
    {
        struct BoatStep
        {
            public uint Serial, Time;
            public ushort X, Y;
            public sbyte Z;
            public byte Speed;
            public Direction MovingDir, FacingDir;
        }

        struct ItemInside
        {
            public uint Serial;
            public int X, Y, Z;
        }



        private static readonly Dictionary<uint, Deque<BoatStep>> _steps = new Dictionary<uint, Deque<BoatStep>>();
        private static readonly List<uint> _toRemove = new List<uint>();
        private static readonly Dictionary<uint, RawList<ItemInside>> _items = new Dictionary<uint, RawList<ItemInside>>();


        public static void AddStep(uint serial, byte speed, Direction movingDir, Direction facingDir, ushort x, ushort y, sbyte z)
        {
            Item item = World.Items.Get(serial);
            if (item == null || item.IsDestroyed)
            {
                return;
            }


            if (!_steps.TryGetValue(serial, out var deque))
            {
                deque = new Deque<BoatStep>();
                _steps[serial] = deque;
            }

            while (deque.Count >= Constants.MAX_STEP_COUNT)
            {
                deque.RemoveFromFront();
            }


            GetEndPosition(
                item,
                deque,
                out ushort currX,
                out ushort currY,
                out sbyte currZ,
                out Direction endDir);

            if (currX == x && currY == y && currZ == z && endDir == movingDir)
            {
                return;
            }

            if (deque.Count == 0)
            {
                Console.WriteLine("SET TIMER");
                item.LastStepTime = Time.Ticks;
            }


            Direction moveDir = DirectionHelper.CalculateDirection(currX, currY, x, y);

            BoatStep step = new BoatStep();
            step.Serial = serial;
            step.Time = Time.Ticks;
            step.Speed = speed;

            if (moveDir != Direction.NONE)
            {
                if (moveDir != endDir)
                {
                    step.X = currX;
                    step.Y = currY;
                    step.Z = currZ;
                    step.MovingDir = moveDir;
                    deque.AddToBack(step);
                }

                step.X = x;
                step.Y = y;
                step.Z = z;
                step.MovingDir = moveDir;
                deque.AddToBack(step);
            }

            if (moveDir != movingDir)
            {
                step.X = x;
                step.Y = y;
                step.Z = z;
                step.MovingDir = movingDir;
                deque.AddToBack(step);
            }

            Console.WriteLine(">>> STEP ADDED {0}", speed);
        }

        public static void ClearSteps(uint serial)
        {
            if (_steps.TryGetValue(serial, out var deque) && deque.Count != 0)
            {
                Item multiItem = World.Items.Get(serial);

                if (multiItem != null)
                {
                    multiItem.Offset.X = 0;
                    multiItem.Offset.Y = 0;
                    multiItem.Offset.Z = 0;
                }

                if (_items.TryGetValue(serial, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        ref var it = ref list[i];

                        Entity ent = World.Get(it.Serial);

                        ent.Offset.X = 0;
                        ent.Offset.Y = 0;
                        ent.Offset.Z = 0;
                    }

                    list.Clear();
                }

                deque.Clear();
            }
        }

        public static void PushItemToList(uint serial, uint objSerial, int x, int y, int z)
        {
            if (!_items.TryGetValue(serial, out var list))
            {
                list = new RawList<ItemInside>();

                _items[serial] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                ref var item = ref list[i];

                if (item.Serial == objSerial)
                {
                    item.X = x;
                    item.Y = y;
                    item.Z = z;
                    return;
                }
            }

            list.Add(new ItemInside()
            {
                Serial = objSerial,
                X = x,
                Y = y,
                Z = z
            });
        }

        public static void Update()
        {
            foreach (Deque<BoatStep> deques in _steps.Values)
            {
                while (deques.Count != 0)
                {
                    ref var step = ref deques.Front();

                    Item item = World.Items.Get(step.Serial);

                    if (item == null || item.IsDestroyed)
                    {
                        _toRemove.Add(step.Serial);
                        break;
                    }

                    int maxDelay = step.Speed <= 2 ? 1000 : 250;
                    int delay = (int)Time.Ticks - (int)item.LastStepTime;
                    bool removeStep = delay >= maxDelay;
                    bool directionChange = false;


                    if (/*step.FacingDir == step.MovingDir &&*/
                        (item.X != step.X || item.Y != step.Y))
                    {
                        float steps = maxDelay / (float)Constants.CHARACTER_ANIMATION_DELAY;
                        float x = delay / (float)Constants.CHARACTER_ANIMATION_DELAY;
                        float y = x;
                        item.Offset.Z = (sbyte)((step.Z - item.Z) * x * (4.0f / steps));
                        MovementSpeed.GetPixelOffset((byte)step.MovingDir, ref x, ref y, steps);
                        item.Offset.X = (sbyte)x;
                        item.Offset.Y = (sbyte)y;
                    }
                    else
                    {
                        directionChange = true;
                        removeStep = true;
                    }

                    World.HouseManager.TryGetHouse(item, out House house);

                    if (removeStep)
                    {
                        item.X = step.X;
                        item.Y = step.Y;
                        item.Z = step.Z;
                        item.UpdateScreenPosition();

                        item.Offset.X = 0;
                        item.Offset.Y = 0;
                        item.Offset.Z = 0;

                        deques.RemoveFromFront();

                        if (directionChange)
                        {
                            continue;
                        }

                        if (item.Right != null || item.Left != null)
                            item.AddToTile();

                        house?.Generate(true, true, true);
                        UpdateEntitiesInside(item, removeStep, step.X, step.Y, step.Z);

                        item.LastStepTime = Time.Ticks;
                    }
                    else
                    {
                        if (house != null)
                        {
                            bool preview = step.MovingDir != Direction.West &&
                                    step.MovingDir != Direction.Up &&
                                    step.MovingDir != Direction.North;

                            foreach (var c in house.Components)
                            {
                                c.Offset = item.Offset;

                                if (preview)
                                    c.State |= CUSTOM_HOUSE_MULTI_OBJECT_FLAGS.CHMOF_PREVIEW;
                            }
                        }

                        UpdateEntitiesInside(item, removeStep, item.X, item.Y, item.Z);
                    }


                    break;
                }
            }


            if (_toRemove.Count != 0)
            {
                for (int i = 0; i < _toRemove.Count; i++)
                {
                    _steps.Remove(_toRemove[i]);
                    _items.Remove(_toRemove[i]);
                }

                _toRemove.Clear();
            }
        }

        private static void UpdateEntitiesInside(uint serial, bool removeStep, int x, int y, int z)
        {
            if (_items.TryGetValue(serial, out var list))
            {
                Item item = World.Items.Get(serial);

                for (int i = 0; i < list.Count; i++)
                {
                    ref var it = ref list[i];

                    Entity entity = World.Get(it.Serial);
                    if (entity == null || entity.IsDestroyed)
                    {
                        list.RemoveAt((uint)i--);
                        continue;
                    }

                    if (removeStep)
                    {
                        entity.X = (ushort)(x - it.X);
                        entity.Y = (ushort)(y - it.Y);
                        entity.Z = (sbyte)(z - it.Z);

                        if (entity == World.Player)
                        {
                            World.RangeSize.X = entity.X;
                            World.RangeSize.Y = entity.Y;
                        }

                        entity.UpdateScreenPosition();

                        entity.Offset.X = 0;
                        entity.Offset.Y = 0;
                        entity.Offset.Z = 0;

                        if (entity.Left != null || entity.Right != null)
                            entity.AddToTile();
                    }
                    else
                    {
                        if (item != null)
                        {
                            entity.Offset = item.Offset;
                        }
                    }

                }
            }
        }

        private static void GetEndPosition(Item item, Deque<BoatStep> deque, out ushort x, out ushort y, out sbyte z, out Direction dir)
        {
            if (deque.Count == 0)
            {
                x = item.X;
                y = item.Y;
                z = item.Z;
                dir = item.Direction & Direction.Up;
                dir &= Direction.Running;
            }
            else
            {
                ref var s = ref deque.Back();
                x = s.X;
                y = s.Y;
                z = s.Z;
                dir = s.MovingDir;
            }
        }
    }
}