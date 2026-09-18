using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fslop.SpikeB.EditorTools
{
    /// <summary>
    /// Gera a cena do spike da candidata B a partir de codigo.
    ///
    /// O arquivo .unity versionado e PRODUTO deste script (decisions/07 da task). Editar
    /// a cena pelo Editor sem portar a alteracao para ca significa perde-la na proxima
    /// geracao. O motivo e a change 07: 150 corpos precisam nascer em posicao
    /// deterministica para duas corridas do soak poderem comparar world_hash.
    ///
    /// Rodar: Unity.exe -projectPath ... -batchmode -quit -nographics
    ///        -executeMethod Fslop.SpikeB.EditorTools.SpikeSceneBuilder.Build
    /// </summary>
    public static class SpikeSceneBuilder
    {
        public const string SceneFolder = "Assets/Scenes";
        public const string ScenePath = SceneFolder + "/SpikeB.unity";

        public const string PhysicsFolder = "Assets/Physics";
        public const string NetworkFolder = "Assets/Network";
        public const string PrefabCollectionPath = NetworkFolder + "/SpikePrefabs.asset";
        public const string PlayerMaterialPath = PhysicsFolder + "/PlayerFrictionless.asset";

        public const string GroundName = "Ground";
        public const string PlayerName = "Player";
        public const string CameraName = "MainCamera";
        public const string SunName = "Sun";
        public const string BoxStackName = "BoxStack";
        public const string BeamName = "Beam";
        public const string SoakRunnerName = "SoakRunner";
        public const string NetworkName = "NetworkManager";

        // Medidas em unidades do motor. O contrato de medicao fixa 1 u = 1 m, entao
        // massa em kg e altura em metros sao a mesma escala e nao ha conversao escondida.
        // 80 u, e o tamanho e resultado de medicao: com 40 u, o desabamento da torre de
        // 15 u de altura jogava caixas para FORA da plataforma, e elas caiam no vazio. O
        // teste "atravessou o chao" nao consegue distinguir isso de um bug de tunelamento
        // do PhysX, entao o chao passou a ser grande o bastante para o entulho caber nele.
        const float GroundSide = 80f;
        const float GroundThickness = 1f;
        const float PlayerSpawnHeight = 5f;
        const float PlayerMassKg = 70f;

        [MenuItem("FSLOP/Gerar cena do spike B")]
        public static void Build()
        {
            var playerMaterial = CreatePlayerMaterial();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateGround();
            CreateSun();
            CreatePlayer(playerMaterial);
            CreateCamera();
            CreateBoxStack();
            CreateBeam();
            CreateNetwork();
            CreateSoakRunner();

            if (!AssetDatabase.IsValidFolder(SceneFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            AtribuirIdsDeCena(scene);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new System.Exception("[BUILDER] SaveScene falhou para " + ScenePath);
            }

            // A cena precisa estar na lista de build porque a change 09 sobe o player
            // headless, e player headless nao tem quem escolha cena.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Log("cena escrita em " + ScenePath);
            Log("raizes=" + scene.rootCount);
            Log("material do player=" + PlayerMaterialPath);
        }

        /// <summary>
        /// Grava o id de cena de cada NetworkObject ANTES de salvar o .unity.
        ///
        /// Por que isto existe (errors/06 da task): o FishNet gera esse id sozinho, pelo
        /// OnValidate do NetworkObject, mas lido em NetworkObject.Serialized.cs:167 ele
        /// desiste quando `gameObject.scene.name` esta vazio — e a cena criada por
        /// EditorSceneManager.NewScene so ganha nome quando e salva. Ou seja: no momento
        /// do AddComponent a cena ainda nao tem nome, o FishNet conclui "isto nao e objeto
        /// de cena" e ZERA o id. O SaveScene depois grava o zero. No player, o
        /// NetworkObject.cs:392 encontra SceneId == 0 e recusa inicializar o objeto — foi
        /// assim que a viga sumiu das duas pontas.
        ///
        /// O utilitario oficial (menu Fish-Networking > Utility > Reserialize
        /// NetworkObjects) nao serve aqui: ReserializeNetworkObjectsEditor.cs:64 e
        /// `internal`, invisivel para este assembly. A porta publica e
        /// NetworkObject.Serialized.cs:37, `public void SetSceneId(ulong)`.
        ///
        /// O id nao precisa ser o que o FishNet sortearia; precisa ser NAO-ZERO, unico
        /// dentro da cena e IGUAL nos dois processos. Como host e cliente carregam o mesmo
        /// arquivo de cena, qualquer valor gravado serve. Deterministico de proposito: o
        /// .unity e versionado e nao pode mudar de diff a cada regeracao — o sorteio do
        /// FishNet (Serialized.cs:138, System.Random) mudaria.
        /// </summary>
        static void AtribuirIdsDeCena(UnityEngine.SceneManagement.Scene scene)
        {
            var atribuidos = new Dictionary<ulong, string>();

            foreach (var raiz in scene.GetRootGameObjects())
            {
                // includeInactive: true — objeto de cena em rede fica desativado no cliente
                // ate o spawn, e um dia isso vai valer tambem no arquivo salvo.
                foreach (var nob in raiz.GetComponentsInChildren<FishNet.Object.NetworkObject>(true))
                {
                    string caminho = CaminhoNaHierarquia(nob.transform);
                    ulong id = Fnv1a32(caminho);

                    // Zero e o valor "nao atribuido" (NetworkObject.cs:360). Se o hash cair
                    // nele, o objeto ficaria invisivel de novo — e em silencio.
                    if (id == 0UL)
                    {
                        id = 1UL;
                    }

                    if (atribuidos.TryGetValue(id, out string jaUsadoPor))
                    {
                        throw new System.Exception(string.Format(CultureInfo.InvariantCulture,
                            "[BUILDER] colisao de id de cena entre '{0}' e '{1}' (id={2}). " +
                            "Renomeie um dos dois: o id vem do caminho na hierarquia.",
                            jaUsadoPor, caminho, id));
                    }

                    atribuidos.Add(id, caminho);

                    nob.SetSceneId(id);
                    EditorUtility.SetDirty(nob);

                    Log(string.Format(CultureInfo.InvariantCulture,
                        "id de cena {0} = {1}", caminho, id));
                }
            }

            if (atribuidos.Count == 0)
            {
                throw new System.Exception(
                    "[BUILDER] nenhum NetworkObject na cena. A viga deveria ter um " +
                    "(CreateBeam) — sem ele nao ha o que replicar e o soak mede o vazio.");
            }
        }

        static string CaminhoNaHierarquia(Transform t)
        {
            string caminho = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                caminho = p.name + "/" + caminho;
            }

            return caminho;
        }

        /// <summary>
        /// FNV-1a 32 bits. Mesma funcao que o SoakRunner usa para o world_hash, pelo mesmo
        /// motivo: e curta o bastante para eu conferir a mao e nao depende de biblioteca.
        /// </summary>
        static ulong Fnv1a32(string texto)
        {
            uint hash = 2166136261u;
            foreach (char c in texto)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return hash;
        }

        /// <summary>
        /// Chao com o TOPO exatamente em y = 0. Assim toda altura medida pela sonda e
        /// altura acima do chao, sem subtrair espessura em lugar nenhum.
        /// </summary>
        static void CreateGround()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = GroundName;
            go.transform.localScale = new Vector3(GroundSide, GroundThickness, GroundSide);
            go.transform.position = new Vector3(0f, -GroundThickness * 0.5f, 0f);
        }

        static void CreateSun()
        {
            var go = new GameObject(SunName);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
        }

        /// <summary>
        /// Capsula primitiva do Unity: raio 0.5, altura 2, centro no meio. Com o topo do
        /// chao em y = 0, o repouso teorico do centro e y = 1.0 — esse e o numero que a
        /// sonda confere.
        /// </summary>
        /// <summary>
        /// Material sem atrito para a capsula (decisions/08). O motor ja controla a
        /// velocidade horizontal por inteiro; o atrito do PhysX e um SEGUNDO controlador
        /// no mesmo eixo, e mediu-se que ele tira mu*g*dt = 0.6*9.81*0.02 = 0.1177 u/s
        /// da velocidade comandada a cada passo. Combine Minimum faz o zero da capsula
        /// vencer o material de qualquer superficie — inclusive as 150 caixas da change
        /// 07, para a velocidade nao depender de em cima de qual caixa o jogador esta.
        /// </summary>
        static PhysicsMaterial CreatePlayerMaterial()
        {
            if (!AssetDatabase.IsValidFolder(PhysicsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Physics");
            }

            var material = new PhysicsMaterial("PlayerFrictionless")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

            AssetDatabase.DeleteAsset(PlayerMaterialPath);
            AssetDatabase.CreateAsset(material, PlayerMaterialPath);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PlayerMaterialPath);
        }

        static void CreatePlayer(PhysicsMaterial material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = PlayerName;
            go.transform.position = new Vector3(0f, PlayerSpawnHeight, 0f);

            go.GetComponent<CapsuleCollider>().sharedMaterial = material;

            var body = go.AddComponent<Rigidbody>();
            body.mass = PlayerMassKg;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // Tombar nao e mecanica: o giro do personagem e do controle, nao do solver.
            body.freezeRotation = true;

            // collisionDetectionMode fica no default (Discrete) DE PROPOSITO. Caindo de
            // 5 m a capsula chega a ~9.9 u/s, ~0.2 u por passo de 0.02 s, contra 1.0 u de
            // meia-altura: nao ha tunelamento a evitar. Ligar CCD aqui seria otimizar
            // antes de medir, que o briefing proibe.

            // Motor e controle sao dois componentes porque so o controle desaparece
            // quando a rede entrar: o cliente remoto tem motor, nao tem teclado.
            go.AddComponent<CapsuleMotor>();
            go.AddComponent<CapsuleController>();
        }

        /// <summary>
        /// So o objeto vazio com o spawner. As 150 caixas NAO ficam salvas na cena — quem
        /// as cria e o BoxStackSpawner, sempre nas mesmas posicoes (ver o cabecalho dele).
        /// Longe do spawn do jogador de proposito: a capsula cai de y=5 e nao pode
        /// derrubar a pilha antes de a medicao comecar.
        /// </summary>
        static void CreateBoxStack()
        {
            var go = new GameObject(BoxStackName);
            go.transform.position = new Vector3(10f, 0f, 0f);
            go.AddComponent<BoxStackSpawner>();
        }

        /// <summary>
        /// A viga de 6 m do teste minimo. Deitada no chao, longe do spawn do jogador e da
        /// pilha (que fica em x=+10): a sonda precisa medir o agarre sem nada esbarrando.
        /// </summary>
        static void CreateBeam()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = BeamName;
            go.transform.localScale = new Vector3(CarryBeam.LengthU, 0.5f, 0.5f);
            go.transform.position = new Vector3(-10f, 0.25f, 0f);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 120f;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            go.AddComponent<CarryBeam>();

            // A viga e um objeto de CENA em rede, nao um prefab spawnado: as duas instancias
            // carregam a mesma cena, e o FishNet casa objetos de cena por id. Sem dono, quem
            // escreve o transform e o servidor — lido em NetworkTransform.cs:1100, onde
            // `canSet` inclui (IsServerInitialized && _clientAuthoritative && !Owner.IsValid).
            go.AddComponent<FishNet.Object.NetworkObject>();

            var sync = go.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
            sync.SetSynchronizeScale(false);   // a viga nao muda de escala; byte a menos por pacote
        }

        /// <summary>
        /// O NetworkManager do FishNet. So o componente raiz e adicionado aqui: lido em
        /// NetworkManager.cs, ele cria sozinho os managers que faltam (GetOrCreateComponent,
        /// linhas 320-335), e o TransportManager adiciona o Tugboat sozinho quando nao acha
        /// nenhum Transport no objeto (TransportManager.cs:268). Montar tudo a mao aqui
        /// duplicaria o que a biblioteca ja faz, e duplicacao envelhece a cada versao dela.
        ///
        /// Tugboat e transporte UDP local. E o que decisions/01 previu para o soak
        /// automatizado; o relay da Steam continua sendo prova manual de 2 maquinas.
        /// </summary>
        static void CreateNetwork()
        {
            var go = new GameObject(NetworkName);
            var manager = go.AddComponent<FishNet.Managing.NetworkManager>();

            // SpawnablePrefabs nao pode ficar nulo: NetworkManager.ValidateSpawnablePrefabs
            // aborta a inicializacao. No Editor ele se vira sozinho buscando o
            // DefaultPrefabObjects, mas isso nao acontece num player — e o player e onde o
            // soak roda. A colecao entra vazia de propósito: a viga e objeto de CENA, nao
            // prefab, e nada e spawnado por instanciacao ainda.
            manager.SpawnablePrefabs = CreatePrefabCollection();
        }

        static FishNet.Managing.Object.PrefabObjects CreatePrefabCollection()
        {
            if (!AssetDatabase.IsValidFolder(NetworkFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Network");
            }

            var colecao = ScriptableObject.CreateInstance<FishNet.Managing.Object.SinglePrefabObjects>();

            AssetDatabase.DeleteAsset(PrefabCollectionPath);
            AssetDatabase.CreateAsset(colecao, PrefabCollectionPath);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(
                PrefabCollectionPath);
        }

        /// <summary>
        /// O emissor do contrato. So faz efeito em play mode: as sondas de edit mode rodam
        /// Physics.Simulate direto e nunca chegam a um Start(). E por isso que ele pode
        /// ficar na cena sem atrapalhar nenhuma medicao anterior.
        /// </summary>
        static void CreateSoakRunner()
        {
            var go = new GameObject(SoakRunnerName);
            go.AddComponent<SoakRunner>();
        }

        static void CreateCamera()
        {
            var go = new GameObject(CameraName) { tag = CameraName };
            go.transform.position = new Vector3(0f, 6f, -12f);
            go.transform.rotation = Quaternion.Euler(20f, 0f, 0f);

            go.AddComponent<Camera>();

            // Sem AudioListener: o briefing proibe audio. O Unity avisa que nao ha
            // listener na cena, e o aviso e esperado.
        }

        static void Log(string mensagem)
        {
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[BUILDER] {0}", mensagem));
        }
    }
}
