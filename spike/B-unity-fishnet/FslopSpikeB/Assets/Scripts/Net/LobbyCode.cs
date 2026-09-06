using System;
using System.Text;
using Steamworks;

namespace Fslop.SpikeB
{
    /// <summary>
    /// Converte o SteamID de um lobby em um codigo curto que uma pessoa consegue ditar
    /// por voz, e de volta.
    ///
    /// Existe por causa de decisions/02: a entrada em sala e por convite ou por CODIGO,
    /// nunca varrendo a lista publica de lobbies — no AppID 480 essa lista e compartilhada
    /// com o mundo inteiro.
    ///
    /// So os 32 bits de accountID viajam no codigo. O resto do SteamID de um lobby e
    /// constante (tipo Chat, flag de lobby na instancia, universo do cliente) e e
    /// remontado na leitura. Isso e uma SUPOSICAO sobre o formato do SteamID, e por isso
    /// o SteamLobbyProbe confere o ida-e-volta contra o id64 real que a Steam devolveu:
    /// se a suposicao estiver errada, a sonda reprova em vez de gerar codigo quebrado.
    /// </summary>
    public static class LobbyCode
    {
        /// <summary>
        /// Base32 de Crockford: sem I, L, O e U. Os tres primeiros porque se confundem com
        /// 1 e 0 quando alguem le em voz alta; o U para nao formar palavrao por acaso.
        /// </summary>
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>32 bits de accountID a 5 bits por digito = 6.4, arredondado para 7.</summary>
        public const int Digits = 7;

        public static string From(CSteamID lobby)
        {
            uint accountId = lobby.GetAccountID().m_AccountID;

            var buffer = new char[Digits];
            for (int i = Digits - 1; i >= 0; i--)
            {
                buffer[i] = Alphabet[(int)(accountId & 0x1F)];
                accountId >>= 5;
            }

            // Grupo de 3 e grupo de 4: e mais facil ditar e conferir do que sete seguidos.
            return new string(buffer, 0, 3) + "-" + new string(buffer, 3, 4);
        }

        public static bool TryParse(string code, EUniverse universe, out CSteamID lobby)
        {
            lobby = default;

            string limpo = Normalize(code);
            if (limpo.Length != Digits)
            {
                return false;
            }

            uint accountId = 0;
            foreach (char c in limpo)
            {
                int valor = Alphabet.IndexOf(c);
                if (valor < 0)
                {
                    return false;
                }

                accountId = (accountId << 5) | (uint)valor;
            }

            lobby = new CSteamID(
                new AccountID_t(accountId),
                LobbyInstance,
                universe,
                EAccountType.k_EAccountTypeChat);

            return true;
        }

        /// <summary>
        /// Os bits de instancia de um lobby de matchmaking: 0x60000.
        ///
        /// Este valor foi MEDIDO, nao deduzido. A primeira versao usava so
        /// k_EChatInstanceFlagLobby (0x40000), que e o nome obvio, e o codigo reconstruia
        /// 109212294808055699 em vez de 109775244761477011. A Steam devolveu 0x60000 no
        /// lobby real: um lobby criado por SteamMatchmaking carrega os DOIS flags.
        ///
        /// Continua sendo suposicao sobre formato de terceiro, e por isso o
        /// SteamLobbyProbe confere o ida-e-volta contra o id64 real toda corrida — se a
        /// Valve mudar, a sonda reprova com o valor novo no log em vez de gerar codigo
        /// que leva a lobby nenhum.
        /// </summary>
        public const uint LobbyInstance =
            (uint)EChatSteamIDInstanceFlags.k_EChatInstanceFlagLobby
            | (uint)EChatSteamIDInstanceFlags.k_EChatInstanceFlagMMSLobby;

        /// <summary>
        /// Tolera o que uma pessoa erra ao digitar o que outra ditou: minusculas, hifens,
        /// espacos, e as tres letras que a base32 de Crockford tirou justamente por serem
        /// confundidas.
        /// </summary>
        static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(Digits);
            foreach (char bruto in code)
            {
                char c = char.ToUpperInvariant(bruto);

                switch (c)
                {
                    case 'I':
                    case 'L':
                        c = '1';
                        break;
                    case 'O':
                        c = '0';
                        break;
                }

                if (Alphabet.IndexOf(c) >= 0)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
