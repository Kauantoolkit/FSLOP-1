using System;
using Steamworks;

namespace Fslop.SpikeB
{
    /// <summary>
    /// Sobe a Steam API e cria/entra em lobby. NAO e MonoBehaviour de proposito, pela
    /// mesma razao do CapsuleMotor: a sonda precisa dirigir isto fora do play mode, e o
    /// soak da Fase 3 precisa dirigir sem ninguem na maquina.
    ///
    /// Quem chama e responsavel por bater Pump() com alguma frequencia — a Steam entrega
    /// callback so quando SteamAPI.RunCallbacks() roda. Em play mode isso mora num
    /// Update(); na sonda, num laco com Sleep.
    ///
    /// AppID 480 (Spacewar) por decisions/02. Entrada por convite ou por codigo, nunca
    /// varrendo a lista publica — no 480 essa lista e do mundo inteiro.
    /// </summary>
    public class SteamLobbyService : IDisposable
    {
        public const uint SpikeAppId = 480;

        /// <summary>4 jogadores, como o briefing manda.</summary>
        public const int MaxMembers = 4;

        Callback<LobbyEnter_t> lobbyEnter;
        CallResult<LobbyCreated_t> lobbyCreated;

        public bool Ready { get; private set; }
        public string LastError { get; private set; }

        public bool CreateCompleted { get; private set; }
        public EResult CreateResult { get; private set; }
        public CSteamID CreatedLobby { get; private set; }

        public bool EnterCompleted { get; private set; }
        public CSteamID EnteredLobby { get; private set; }
        public uint EnterResponse { get; private set; }

        public CSteamID LocalUser => Ready ? SteamUser.GetSteamID() : default;
        public EUniverse Universe => Ready ? SteamUser.GetSteamID().GetEUniverse() : EUniverse.k_EUniverseInvalid;

        /// <summary>
        /// A Steam so aceita init se souber contra qual AppID. Fora do cliente Steam isso
        /// vem de um steam_appid.txt no diretorio de trabalho — que em batchmode nao e
        /// necessariamente o do projeto. A variavel de ambiente resolve os dois casos, e e
        /// lida pela DLL nativa no momento do init, entao tem que ser posta ANTES.
        /// </summary>
        public bool Init()
        {
            Environment.SetEnvironmentVariable("SteamAppId", SpikeAppId.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", SpikeAppId.ToString());

            string erro;
            var resultado = SteamAPI.InitEx(out erro);

            if (resultado != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                LastError = resultado + ": " + erro;
                Ready = false;
                return false;
            }

            // Os handlers so podem ser criados DEPOIS do init: e ele que liga o
            // CallbackDispatcher que os entrega.
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);

            Ready = true;
            LastError = null;
            return true;
        }

        public void Pump()
        {
            if (Ready)
            {
                SteamAPI.RunCallbacks();
            }
        }

        /// <summary>
        /// FriendsOnly, e nao Public, por decisions/02: no AppID 480 um lobby publico entra
        /// numa lista que qualquer pessoa do mundo consulta. FriendsOnly nao aparece em
        /// busca e continua aceitando entrada por id — que e o que o codigo carrega.
        /// </summary>
        public void RequestCreateLobby()
        {
            CreateCompleted = false;
            CreateResult = EResult.k_EResultNone;
            CreatedLobby = default;

            var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxMembers);
            lobbyCreated.Set(call);
        }

        public void RequestJoin(CSteamID lobby)
        {
            EnterCompleted = false;
            EnteredLobby = default;
            SteamMatchmaking.JoinLobby(lobby);
        }

        public void Leave(CSteamID lobby)
        {
            if (Ready && lobby.m_SteamID != 0)
            {
                SteamMatchmaking.LeaveLobby(lobby);
            }
        }

        void OnLobbyCreated(LobbyCreated_t param, bool ioFailure)
        {
            CreateCompleted = true;
            CreateResult = ioFailure ? EResult.k_EResultIOFailure : param.m_eResult;
            CreatedLobby = new CSteamID(param.m_ulSteamIDLobby);
        }

        void OnLobbyEnter(LobbyEnter_t param)
        {
            EnterCompleted = true;
            EnteredLobby = new CSteamID(param.m_ulSteamIDLobby);
            EnterResponse = param.m_EChatRoomEnterResponse;
        }

        public void Dispose()
        {
            if (!Ready)
            {
                return;
            }

            if (lobbyEnter != null)
            {
                lobbyEnter.Dispose();
                lobbyEnter = null;
            }

            if (lobbyCreated != null)
            {
                lobbyCreated.Dispose();
                lobbyCreated = null;
            }

            SteamAPI.Shutdown();
            Ready = false;
        }
    }
}
