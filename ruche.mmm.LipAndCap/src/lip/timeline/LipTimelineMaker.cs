﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace ruche.mmm.lip.timeline
{
    /// <summary>
    /// 同じ口形状かつ LinkType.Normal のユニットが連続する場合の
    /// 接続ウェイト値を決定するデリゲート。
    /// </summary>
    /// <param name="before">先行ユニット。</param>
    /// <param name="after">後続ユニット。</param>
    /// <returns>接続ウェイト値。</returns>
    public delegate float SameLipLinkWeightDecider(
        LipSyncUnit before,
        LipSyncUnit after);

    /// <summary>
    /// 口形状種別ごとのタイムラインを作成するクラス。
    /// </summary>
    public class LipTimelineMaker
    {
        /// <summary>
        /// 次の音と重なる長さのユニット長に対するパーセント値の既定値。
        /// </summary>
        public static readonly int DefaultLinkLengthPercent = 50;

        /// <summary>
        /// 次の音と重なる長さのユニット長に対するパーセント値の最小値。
        /// </summary>
        public static readonly int LinkLengthPercentMin = 0;

        /// <summary>
        /// 次の音と重なる長さのユニット長に対するパーセント値の最大値。
        /// </summary>
        public static readonly int LinkLengthPercentMax = 100;

        /// <summary>
        /// 長音の最終位置におけるウェイト値の既定値。
        /// </summary>
        public static readonly float DefaultLongSoundLastWeight = 1.0f;

        /// <summary>
        /// 同じ口形状かつ LinkType.Normal のユニットが連続する場合の
        /// 接続ウェイト値の既定値を決定する。
        /// </summary>
        /// <param name="before">先行ユニット。</param>
        /// <param name="after">後続ユニット。</param>
        /// <returns>接続ウェイト値の既定値。</returns>
        private static float DecideDefaultSameLipLinkWeight(
            LipSyncUnit before,
            LipSyncUnit after)
        {
            switch (after.LipId)
            {
            case LipId.I:
                return 1.0f;

            case LipId.U:
                return 0.9f;
            }

            return 0.7f;
        }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public LipTimelineMaker()
        {
            this.LinkLengthPercent = DefaultLinkLengthPercent;
            this.LongSoundLastWeight = DefaultLongSoundLastWeight;
        }

        /// <summary>
        /// 次の音と重なる長さのユニット長に対するパーセント値を取得または設定する。
        /// </summary>
        /// <remarks>
        /// 開口開始からこの長さで最大開口に達し、ユニット長分まで開口を続け、
        /// この長さで閉口する。
        /// つまり実際の1ユニットの動作時間はこの長さ分だけ増えることになる。
        /// </remarks>
        public int LinkLengthPercent
        {
            get { return _linkLengthPercent; }
            set
            {
                _linkLengthPercent =
                    Math.Min(
                        Math.Max(LinkLengthPercentMin, value),
                        LinkLengthPercentMax);
            }
        }
        private int _linkLengthPercent = DefaultLinkLengthPercent;

        /// <summary>
        /// 長音の最大開口位置におけるウェイト値を取得または設定する。
        /// </summary>
        public float LongSoundLastWeight { get; set; }

        /// <summary>
        /// 同じ口形状かつ LinkType.Normal のユニットが連続する場合の
        /// 接続ウェイト値を決定するデリゲートを取得または設定する。
        /// </summary>
        public SameLipLinkWeightDecider SameLipLinkWeightDecider { get; set; }

        /// <summary>
        /// リップシンクユニットリストから口形状種別ごとのタイムラインを作成する。
        /// </summary>
        /// <param name="units">リップシンクユニットリスト。</param>
        /// <returns>口形状種別ごとのタイムライン。</returns>
        public LipTimelineSet Make(IEnumerable<LipSyncUnit> units)
        {
            var tlSet = new LipTimelineSet();
            var areaUnits = new List<LipSyncUnit>();
            LipSyncUnit prevUnit = null;
            long place = 0;

            foreach (var unit in units)
            {
                // 次の音に入ったか？
                if (unit.LinkType.HasSingleSound())
                {
                    // ここまでの音をタイムラインに追加
                    place = AddAreaUnitList(tlSet, place, areaUnits, prevUnit, unit);
                    prevUnit = areaUnits.FirstOrDefault();
                    areaUnits.Clear();
                }

                // キー領域ユニット追加
                areaUnits.Add(unit);
            }

            // 最後の音をタイムラインに追加
            AddAreaUnitList(tlSet, place, areaUnits, prevUnit, null);

            return tlSet;
        }

        /// <summary>
        /// ユニットリストをキー領域に変換してタイムラインに追加する。
        /// </summary>
        /// <param name="tlSet">追加先のタイムラインセット。</param>
        /// <param name="beginPlace">開始キー位置。</param>
        /// <param name="areaUnits">ユニットリスト。</param>
        /// <param name="prevUnit">先行発声ユニット。無いならば null 。</param>
        /// <param name="nextUnit">後続発声ユニット。無いならば null 。</param>
        /// <returns>後続のキー領域を開始すべきキー位置。</returns>
        private long AddAreaUnitList(
            LipTimelineSet tlSet,
            long beginPlace,
            IList<LipSyncUnit> areaUnits,
            LipSyncUnit prevUnit,
            LipSyncUnit nextUnit)
        {
            if (areaUnits.Count <= 0)
            {
                return beginPlace;
            }

            // 最初のユニットと口形状種別IDを取得
            var unit = areaUnits[0];
            var id = unit.LipId;

            var area = new KeyArea();

            // 開口および閉口の長さを算出
            long openCloseLen = CalcOpenCloseLength(unit);

            // 開始キーを追加
            long openHalfPos = beginPlace + openCloseLen / 2;
            if (prevUnit != null && unit.LinkType != LinkType.Normal)
            {
                // 一旦口を(半分)閉じるならば開口時間の1/2まで経過してから開口開始
                area.AddPointAfter(openHalfPos, 0);
            }
            else if (prevUnit != null && prevUnit.LipId == id)
            {
                // 直前と同じ口形状ならば接続ウェイト値の1/2を設定
                var lw = DecideSameLipLinkWeight(prevUnit, unit);
                area.AddPointAfter(openHalfPos, lw / 2);
            }
            else
            {
                area.AddPointAfter(beginPlace, 0);
            }

            // 最大開口開始キーを追加
            long maxBeginPos = beginPlace + openCloseLen;
            maxBeginPos = area.AddPointAfter(maxBeginPos, 1);

            // 次の音開始位置(＝最大開口終了位置)を算出
            long linkPos = maxBeginPos + CalcMaxOpenLength(unit);

            // 最初のユニットをカット
            var units = areaUnits.Skip(1);

            // 次のユニットを取得
            unit = units.FirstOrDefault();

            // 最大開口終端位置追加済みフラグ
            bool maxEndAdded = false;

            // 長音部分を計算
            if (unit != null && unit.LinkType == LinkType.LongSound)
            {
                // 次の音開始位置をシフト
                foreach (
                    var u in units.TakeWhile(u => u.LinkType == LinkType.LongSound))
                {
                    linkPos += CalcUnitLength(u);
                }

                // 長音終端キーを追加
                linkPos = area.AddPointAfter(linkPos, this.LongSoundLastWeight);
                maxEndAdded = true;

                // 長音部分をカット
                units = units.SkipWhile(u => u.LinkType == LinkType.LongSound);

                // 次のユニットを取得
                unit = units.FirstOrDefault();
            }

            // 促音部分を計算
            if (unit != null && unit.LinkType == LinkType.Tsu)
            {
                // 次の音開始位置をシフト
                foreach (
                    var u in units.TakeWhile(u => u.LinkType == LinkType.Tsu))
                {
                    linkPos += CalcUnitLength(u);
                }

                // 促音終端キーを追加
                linkPos = area.AddPointAfter(linkPos, area.Points.Last().Value);
                maxEndAdded = true;

                // 長音部分をカット
                units = units.SkipWhile(u => u.LinkType == LinkType.Tsu);

                // 次のユニットを取得
                unit = units.FirstOrDefault();
            }

            // 最大開口終端キーが追加されていなければ追加
            if (!maxEndAdded)
            {
                linkPos = area.AddPointAfter(linkPos, area.Points.Last().Value);
            }

            // 継続部分を計算
            // 継続より後ろの長音や促音も継続扱い
            if (unit != null)
            {
                // 次の音開始位置をシフト
                foreach (var u in units)
                {
                    linkPos += CalcUnitLength(u);
                }
            }

            // 終端キーを追加
            long endPos = linkPos + openCloseLen;
            if (nextUnit != null && nextUnit.LinkType == LinkType.PreClose)
            {
                // 一旦口を閉じるならば次の音の開口時間の1/2だけ終端を早める
                long closeHalfPos = endPos - CalcOpenCloseLength(nextUnit) / 2;
                closeHalfPos = Math.Max(closeHalfPos, linkPos);
                area.AddPointAfter(closeHalfPos, 0);
            }
            else if (
                nextUnit != null &&
                nextUnit.LipId == id &&
                nextUnit.LinkType == LinkType.Normal)
            {
                // 後続と同じ口形状ならば接続ウェイト値の1/2を設定
                long closeHalfPos = endPos - CalcOpenCloseLength(nextUnit) / 2;
                closeHalfPos = Math.Max(closeHalfPos, linkPos);
                var lw = DecideSameLipLinkWeight(unit, nextUnit);
                area.AddPointAfter(closeHalfPos, lw / 2);
            }
            else
            {
                area.AddPointAfter(endPos, 0);
            }

            // タイムラインに追加
            tlSet[id].KeyAreas.Add(area);

            // 次の音の開始位置を返す
            return linkPos;
        }

        /// <summary>
        /// 同じ口形状かつ LinkType.Normal のユニットが連続する場合の
        /// 接続ウェイト値を決定する。
        /// </summary>
        /// <param name="before">先行ユニット。</param>
        /// <param name="after">後続ユニット。</param>
        /// <returns>接続ウェイト値。</returns>
        private float DecideSameLipLinkWeight(
            LipSyncUnit before,
            LipSyncUnit after)
        {
            return (this.SameLipLinkWeightDecider == null) ?
                DecideDefaultSameLipLinkWeight(before, after) :
                this.SameLipLinkWeightDecider(before, after);
        }

        /// <summary>
        /// 開口および閉口の長さを算出する。
        /// </summary>
        /// <param name="unit">ユニット。</param>
        /// <returns>開口および閉口の長さ。</returns>
        private long CalcOpenCloseLength(LipSyncUnit unit)
        {
            return (
                LipTimelineSet.LengthPerUnit *
                unit.LengthPercent *
                this.LinkLengthPercent /
                10000L);
        }

        /// <summary>
        /// ユニットの開口開始位置から最大開口終了位置までの長さを算出する。
        /// </summary>
        /// <param name="unit">ユニット。</param>
        /// <returns>開口開始位置から最大開口終了位置までの長さ。</returns>
        private long CalcUnitLength(LipSyncUnit unit)
        {
            return (
                LipTimelineSet.LengthPerUnit *
                unit.LengthPercent /
                100);
        }

        /// <summary>
        /// ユニットの最大開口の長さを算出する。
        /// </summary>
        /// <param name="unit">ユニット。</param>
        /// <returns>最大開口の長さ。</returns>
        private long CalcMaxOpenLength(LipSyncUnit unit)
        {
            return (CalcUnitLength(unit) - CalcOpenCloseLength(unit));
        }
    }
}
