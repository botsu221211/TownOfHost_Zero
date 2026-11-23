using System.Collections.Generic;
using Hazel;

using TownOfHostY.Attributes;
using TownOfHostY.Roles.Core;

namespace TownOfHostY
{
    public static class CustomWinnerHolder
    {
        // 勝者のチームが格納されます。
        // リザルトの背景色の決定などに使用されます。
        // 注: この変数を変更する時、WinnerRoles・WinnerIdsを同時に変更しないと予期せぬ勝者が現れる可能性があります。
        public static CustomWinner WinnerTeam;
        // 追加勝利するプレイヤーの役職が格納されます。
        // リザルトの表示に使用されます。
        public static HashSet<CustomRoles> AdditionalWinnerRoles;
        // 勝者の役職が格納され、この変数に格納されている役職のプレイヤーは全員勝利となります。
        // チームとなるニュートラルの処理に最適です。
        public static HashSet<CustomRoles> WinnerRoles;
        // 勝者のPlayerIDが格納され、このIDを持つプレイヤーは全員勝利します。
        // 単独勝利するニュートラルの処理に最適です。
        public static HashSet<byte> WinnerIds;
        // 役職での単独勝利者PlayerIdが格納されます。
        // ここに登録されてもWinnerIdsに登録されないと勝利しません。
        public static HashSet<byte> NeutralWinnerIds;
        // 問答無用で敗北するPlayerIDが格納されます。
        // 敗者リストは勝者リスト・勝者チームのリストより優先されます。
        public static HashSet<byte> CantWinPlayerIds;
        // 勝利優先順位の影響で勝利した陣営を含む、全勝利陣営が格納されます（ログ用）。
        public static HashSet<CustomWinner> winners;

        [GameModuleInitializer, PluginModuleInitializer]
        public static void Reset()
        {
            WinnerTeam = CustomWinner.Default;
            AdditionalWinnerRoles = new();
            WinnerRoles = new();
            WinnerIds = new();
            NeutralWinnerIds = new();
            CantWinPlayerIds = new();
            winners = new();
            WinPriority = 0;
        }

        /// <summary>
        /// 今の勝者の勝利優先度 (TOH_Zero)。通常の陣営勝利は 0。
        /// 勝利優先度を持つ役職(インフェクショナー・タイガー・チャッターなど)は <see cref="SetPriorityWinner"/> で勝者を決める
        /// </summary>
        public static int WinPriority;

        /// <summary>
        /// 勝利優先度を比べて勝者を決める (TownOfHost_L の ResetAndSetAndChWinner を参考)。
        /// まだ勝者がいないか、今の勝者より優先度が大きければ単独勝利。同じ優先度なら追加勝利(addWin が true の時)。
        /// </summary>
        public static bool SetPriorityWinner(CustomWinner winner, int priority, IEnumerable<byte> playerIds, bool addWin = true)
        {
            if (WinnerTeam is CustomWinner.Default || priority > WinPriority)
            {
                if (WinnerTeam is not CustomWinner.Default)
                    Logger.Info($"勝利優先度 {WinPriority} の {WinnerTeam} を、優先度 {priority} の {winner} が上書き", "WinPriority");
                ResetAndSetWinner(winner);
                WinPriority = priority;
                foreach (var id in playerIds) WinnerIds.Add(id);
                return true;
            }
            if (priority == WinPriority && addWin)
            {
                Logger.Info($"勝利優先度が同じ {priority} なので {winner} が追加勝利", "WinPriority");
                AdditionalWinnerRoles.Add((CustomRoles)winner);
                foreach (var id in playerIds) WinnerIds.Add(id);
                return true;
            }
            return false;
        }
        public static void ClearWinners()
        {
            WinnerRoles.Clear();
            WinnerIds.Clear();
            NeutralWinnerIds.Clear();
            CantWinPlayerIds.Clear();
            winners.Clear();
        }
        /// <summary><para>WinnerTeamに値を代入します。</para><para>すでに代入されている場合、AdditionalWinnerRolesに追加します。</para></summary>
        public static void SetWinnerOrAdditonalWinner(CustomWinner winner)
        {
            if (WinnerTeam == CustomWinner.Default)
            {
                WinnerTeam = winner;
                winners.Add(winner);
            }
            else AdditionalWinnerRoles.Add((CustomRoles)winner);
        }
        /// <summary><para>WinnerTeamに値を代入します。</para><para>すでに代入されている場合、既存の値をAdditionalWinnerRolesに追加してから代入します。</para></summary>
        public static void ShiftWinnerAndSetWinner(CustomWinner winner)
        {
            if (WinnerTeam != CustomWinner.Default)
                AdditionalWinnerRoles.Add((CustomRoles)WinnerTeam);
            WinnerTeam = winner;
            winners.Add(winner);
        }
        /// <summary><para>既存の値をすべて削除してから、WinnerTeamに値を代入します。</para></summary>
        public static void ResetAndSetWinner(CustomWinner winner)
        {
            Reset();
            WinnerTeam = winner;
            winners.Add(winner);
        }

        public static MessageWriter WriteTo(MessageWriter writer)
        {
            writer.WritePacked((int)WinnerTeam);

            writer.WritePacked(AdditionalWinnerRoles.Count);
            foreach (var wr in AdditionalWinnerRoles)
                writer.WritePacked((int)wr);

            writer.WritePacked(WinnerRoles.Count);
            foreach (var wr in WinnerRoles)
                writer.WritePacked((int)wr);

            writer.WritePacked(WinnerIds.Count);
            foreach (var id in WinnerIds)
                writer.Write(id);

            writer.WritePacked(CantWinPlayerIds.Count);
            foreach (var id in CantWinPlayerIds)
                writer.Write(id);

            return writer;
        }
        public static void ReadFrom(MessageReader reader)
        {
            WinnerTeam = (CustomWinner)reader.ReadPackedInt32();

            AdditionalWinnerRoles = new();
            int AdditionalWinnerRolesCount = reader.ReadPackedInt32();
            for (int i = 0; i < AdditionalWinnerRolesCount; i++)
                AdditionalWinnerRoles.Add((CustomRoles)reader.ReadPackedInt32());

            WinnerRoles = new();
            int WinnerRolesCount = reader.ReadPackedInt32();
            for (int i = 0; i < WinnerRolesCount; i++)
                WinnerRoles.Add((CustomRoles)reader.ReadPackedInt32());

            WinnerIds = new();
            int WinnerIdsCount = reader.ReadPackedInt32();
            for (int i = 0; i < WinnerIdsCount; i++)
                WinnerIds.Add(reader.ReadByte());

            CantWinPlayerIds = new();
            int CantWinPlayerIdsCount = reader.ReadPackedInt32();
            for (int i = 0; i < CantWinPlayerIdsCount; i++)
                CantWinPlayerIds.Add(reader.ReadByte());
        }
    }
}
