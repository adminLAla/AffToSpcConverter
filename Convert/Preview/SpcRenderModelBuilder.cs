using System;
using System.Collections.Generic;
using System.Linq;
using AffToSpcConverter.Models;

namespace AffToSpcConverter.Convert.Preview
{
    public static class SpcRenderModelBuilder
    {
        public static RenderModel Build(IEnumerable<ISpcEvent> events)
        {
            var model = new RenderModel();

            foreach (var e in events)
            {
                switch (e)
                {
                    case SpcChart c:
                        model.Bpm = c.Bpm;
                        model.Beats = c.Beats;
                        break;

                    case SpcBpm b:
                        model.BpmChanges.Add((b.TimeMs, b.Bpm, b.Beats));
                        break;

                    case SpcTap t:
                        // tap(time, kind, lane)
                        model.Items.Add(new RenderItem
                        {
                            Type = RenderItemType.GroundTap,
                            TimeMs = t.TimeMs,
                            EndTimeMs = t.TimeMs,
                            Lane = t.LaneIndex,
                            Kind = t.Kind
                        });
                        break;

                    case SpcHold h:
                        model.Items.Add(new RenderItem
                        {
                            Type = RenderItemType.GroundHold,
                            TimeMs = h.TimeMs,
                            EndTimeMs = h.TimeMs + Math.Max(0, h.DurationMs),
                            Lane = h.LaneIndex,
                            Kind = h.Width
                        });
                        break;

                    case SpcFlick f:
                        model.Items.Add(new RenderItem
                        {
                            Type = RenderItemType.SkyFlick,
                            TimeMs = f.TimeMs,
                            EndTimeMs = f.TimeMs,
                            Den = f.Den,
                            X0 = f.PosNum,
                            W0 = f.WidthNum,
                            Dir = f.Dir
                        });
                        break;

                    case SpcSkyArea s:
                        model.Items.Add(new RenderItem
                        {
                            Type = RenderItemType.SkyArea,
                            TimeMs = s.TimeMs,
                            EndTimeMs = s.TimeMs + Math.Max(0, s.DurationMs),

                            Den = s.Den1, // 你的 spc 里 den1/den2 应该一致
                            X0 = s.X1Num,
                            X1 = s.X2Num,
                            W0 = s.W1Num,
                            W1 = s.W2Num,

                            LeftEase = s.LeftEasing,
                            RightEase = s.RightEasing,
                            GroupId = s.GroupId,
                        });
                        break;
                }
            }

            // 画面稳定：按时间排序（同一时间 skyarea/hold 在底层）
            model.Items.Sort((a, b) =>
            {
                int c = a.TimeMs.CompareTo(b.TimeMs);
                if (c != 0) return c;
                return a.Type.CompareTo(b.Type);
            });

            return model;
        }
    }
}