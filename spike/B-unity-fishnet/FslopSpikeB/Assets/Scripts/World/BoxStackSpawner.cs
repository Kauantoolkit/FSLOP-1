using System.Collections.Generic;
using UnityEngine;

namespace Fslop.SpikeB
{
    /// <summary>
    /// Cria a pilha instavel de caixas 1x1 do teste minimo do briefing.
    ///
    /// As caixas NAO ficam salvas na cena: sao criadas por codigo, sempre nas mesmas
    /// posicoes. Dois motivos, e os dois vao importar depois:
    ///
    /// 1. determinismo — duas corridas do soak so podem comparar world_hash se o estado
    ///    inicial for identico, e "identico" se garante com um for, nao com 150 blocos de
    ///    YAML que ninguem revisa (decisions/07);
    /// 2. e o formato que a rede vai precisar — na versao replicada quem cria os corpos e
    ///    o host, e os clientes recebem. Um spawner ja e essa forma; 150 objetos salvos na
    ///    cena teriam que ser desfeitos.
    /// </summary>
    public class BoxStackSpawner : MonoBehaviour
    {
        /// <summary>
        /// 6 camadas de 5x5 = 150. A forma foi MEDIDA, nao escolhida no olho — tres
        /// proporcoes foram testadas e so uma fica de pe (errors/03):
        ///
        ///   5x5x6    6 camadas   max_desloc 0.1406 u, dorme em 1.84 s   DE PE
        ///   5x3x10  10 camadas   max_desloc 21.15 u, dorme em 21.78 s   DESABA SOZINHA
        ///   5x2x15  15 camadas   max_desloc 24.60 u, dorme em 13.36 s   DESABA SOZINHA
        ///
        /// O corte esta na ALTURA, nao na largura: acima de ~6 camadas o solver do PhysX
        /// com iteracoes padrao nao segura pilha de corpos soltos, e ela se desmancha
        /// sozinha sem ninguem encostar. Isso e resultado do spike, nao contratempo — e um
        /// limite da stack, e a candidata C tera que ser medida no mesmo teste.
        /// </summary>
        [SerializeField] int layers = 6;
        [SerializeField] int columnsX = 5;
        [SerializeField] int columnsZ = 5;

        [SerializeField] float boxSize = 1f;

        /// <summary>Folga lateral entre caixas, para elas nao nascerem se tocando.</summary>
        [SerializeField] float gap = 0.02f;

        /// <summary>Folga vertical: cada camada cai um tico e assenta na de baixo.</summary>
        [SerializeField] float layerLift = 0.02f;

        /// <summary>
        /// Inclinacao acumulada por camada, no eixo FINO (z). O valor NAO e chutado: com
        /// deslocamento uniforme d por camada, as k camadas de cima tem centro de massa a
        /// d*(k-1)/2 do apoio, e a pilha so para de pe enquanto isso for menor que meia
        /// caixa. Com 15 camadas: d * 7 < 0.5, ou seja d < 0.071.
        ///
        /// Com 6 camadas: d * 2.5 < 0.5, ou seja d < 0.2. O 0.15 daqui fica dentro, e foi
        /// medido de pe (0.1406 u de deslocamento). A conta estatica, porem, subestima em
        /// pilha alta — com 15 camadas ela permitia 0.071 e nem 0.02 parou de pe. Ver
        /// errors/03.
        /// </summary>
        [SerializeField] float leanPerLayer = 0.15f;

        /// <summary>
        /// 10 kg contra os 70 kg da capsula. A razao de massa importa para o solver: o
        /// default de 1 kg daria 70:1, faixa em que o PhysX comeca a tremer em contato
        /// empilhado. 7:1 e razao mansa. Nao e numero de sensacao, e de estabilidade.
        /// </summary>
        [SerializeField] float boxMassKg = 10f;

        public int PlannedCount => layers * columnsX * columnsZ;

        /// <summary>
        /// Qual caixa empurrar para derrubar a pilha, e para que lado.
        ///
        /// Mora aqui, e nao na sonda, porque depende da forma da pilha: e a caixa da face
        /// de tras (menor z), no meio da largura, na altura de um jogador. O empurrao vai
        /// no sentido +z, que e o mesmo sentido da inclinacao — e o gesto de alguem
        /// correndo contra a pilha, nao um guindaste erguendo pelo topo.
        /// </summary>
        public int ShoveTargetIndex()
        {
            int camada = Mathf.Clamp(Mathf.RoundToInt(1f / (boxSize + layerLift)), 0, layers - 1);
            int ix = columnsX / 2;
            int iz = 0;

            return camada * (columnsX * columnsZ) + ix * columnsZ + iz;
        }

        public Vector3 ShoveDirection => Vector3.forward;

        /// <summary>
        /// Recria a pilha do zero e devolve os corpos. Idempotente de proposito: a sonda
        /// chama isto com a cena ja aberta, e uma segunda chamada nao pode empilhar duas.
        /// </summary>
        public List<Rigidbody> Spawn()
        {
            ClearExisting();

            var bodies = new List<Rigidbody>(PlannedCount);
            float pitch = boxSize + gap;
            int index = 0;

            for (int layer = 0; layer < layers; layer++)
            {
                float leanZ = leanPerLayer * layer;
                float y = boxSize * 0.5f + layer * (boxSize + layerLift);

                for (int ix = 0; ix < columnsX; ix++)
                {
                    for (int iz = 0; iz < columnsZ; iz++)
                    {
                        float x = (ix - (columnsX - 1) * 0.5f) * pitch;
                        float z = (iz - (columnsZ - 1) * 0.5f) * pitch + leanZ;

                        bodies.Add(CreateBox(index++, new Vector3(x, y, z)));
                    }
                }
            }

            return bodies;
        }

        Rigidbody CreateBox(int index, Vector3 localPosition)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = string.Format("Box_{0:D3}", index);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = Vector3.one * boxSize;

            var body = go.AddComponent<Rigidbody>();
            body.mass = boxMassKg;

            // Interpolate fica DESLIGADO aqui, ao contrario da capsula: sao 150 corpos e
            // interpolacao custa por corpo por quadro. Nada foi otimizado — e que ligar
            // custo em 150 corpos sem ter medido seria a decisao arbitraria, nao o
            // contrario. Entra na conta da change 09 se o fps pedir.

            return body;
        }

        void ClearExisting()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var filho = transform.GetChild(i).gameObject;

                if (Application.isPlaying)
                {
                    Destroy(filho);
                }
                else
                {
                    DestroyImmediate(filho);
                }
            }
        }
    }
}
