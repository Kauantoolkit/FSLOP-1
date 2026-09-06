using UnityEngine;

namespace Fslop.SpikeB
{
    /// <summary>
    /// O que o jogador quer neste passo. Existe separado do teclado de proposito: o soak
    /// da Fase 3 precisa de "inputs scriptados" e a metrica input_ms_p99 do contrato
    /// precisa saber a hora em que a intencao entrou. Nada disso e possivel se o motor
    /// ler Input.GetAxis por dentro.
    /// </summary>
    public struct MoveIntent
    {
        /// <summary>Direcao no plano, em espaco de mundo. x = leste, y = norte.</summary>
        public Vector2 Move;

        /// <summary>Pulo pedido desde o passo anterior.</summary>
        public bool JumpQueued;
    }

    /// <summary>
    /// Aplica um MoveIntent no Rigidbody da capsula. Nao le input, nao le camera, nao
    /// sabe se e host ou cliente — so recebe intencao e dt.
    ///
    /// Essa fronteira e o que permite tres coisas que vem depois:
    /// a sonda de edit mode chamar Step sem play mode, o soak headless alimentar intent
    /// scriptada, e a predicao do FishNet reexecutar o mesmo passo na reconciliacao.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class CapsuleMotor : MonoBehaviour
    {
        /// <summary>
        /// 4 u/s NAO e numero solto: docs/00-contrato-de-medicao.md ancora nele o limiar
        /// de teleporte da viga (0.5 u). Mudar aqui obriga a rever o contrato.
        /// </summary>
        [SerializeField] float moveSpeed = 4f;

        /// <summary>
        /// Altura de pulo alvo, em u. PLACEHOLDER: e numero de sensacao, e o briefing
        /// proibe que eu decida design. Esta aqui para a capsula sair do chao, nao
        /// porque 1.2 foi escolhido por alguem.
        /// </summary>
        [SerializeField] float jumpHeight = 1.2f;

        /// <summary>Quao rapido a velocidade horizontal persegue a desejada, em u/s². Tambem PLACEHOLDER.</summary>
        [SerializeField] float acceleration = 40f;

        /// <summary>Folga abaixo dos pes que ainda conta como chao, em u.</summary>
        [SerializeField] float groundProbeDistance = 0.12f;

        [SerializeField] LayerMask groundLayers = ~0;

        readonly Collider[] groundHits = new Collider[8];

        Rigidbody body;
        CapsuleCollider capsule;

        public bool IsGrounded { get; private set; }

        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;

        void Awake()
        {
            Cache();
        }

        /// <summary>
        /// Um passo de fisica. Chamado do FixedUpdate em play mode e direto pela sonda
        /// em edit mode — por isso ele nao pode depender de nada que so o play mode faz.
        /// </summary>
        public void Step(MoveIntent intent, float dt)
        {
            // Awake nao roda em edit mode, e a sonda chama Step de la.
            Cache();

            IsGrounded = ProbeGround();

            Vector3 velocity = body.linearVelocity;

            Vector2 move = intent.Move;
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            Vector3 desired = new Vector3(move.x, 0f, move.y) * moveSpeed;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

            Vector3 delta = desired - horizontal;
            float maxDelta = acceleration * dt;
            if (delta.sqrMagnitude > maxDelta * maxDelta)
            {
                delta = delta.normalized * maxDelta;
            }

            velocity.x = horizontal.x + delta.x;
            velocity.z = horizontal.z + delta.z;

            if (intent.JumpQueued && IsGrounded)
            {
                // v = sqrt(2 g h): a velocidade que atinge jumpHeight no continuo. O
                // passo discreto perde um pouco disso, e a sonda mede quanto.
                velocity.y = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
                IsGrounded = false;
            }

            body.linearVelocity = velocity;
        }

        /// <summary>
        /// Esfera logo abaixo da semiesfera inferior da capsula. Overlap em vez de
        /// raycast porque um raio unico erra a quina de uma caixa 1x1 — e a partir da
        /// change 07 o chao vai ser feito delas.
        /// </summary>
        bool ProbeGround()
        {
            float radius = capsule.radius * 0.9f;
            float bottomOffset = Mathf.Max(capsule.height * 0.5f - capsule.radius, 0f);

            Vector3 center = transform.TransformPoint(capsule.center)
                             + Vector3.down * (bottomOffset + groundProbeDistance);

            int count = Physics.OverlapSphereNonAlloc(
                center, radius, groundHits, groundLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (groundHits[i].attachedRigidbody != body)
                {
                    return true;
                }
            }

            return false;
        }

        void Cache()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            if (capsule == null)
            {
                capsule = GetComponent<CapsuleCollider>();
            }
        }
    }
}
