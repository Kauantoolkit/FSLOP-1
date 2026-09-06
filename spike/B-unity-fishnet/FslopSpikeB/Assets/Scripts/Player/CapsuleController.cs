using UnityEngine;

namespace Fslop.SpikeB
{
    /// <summary>
    /// Traduz teclado em MoveIntent e entrega ao motor. E de proposito que ele nao faca
    /// mais nada: quando a rede entrar (change 06 em diante), e este objeto que some do
    /// lado do cliente remoto, e o CapsuleMotor continua igual.
    ///
    /// Le o Input Manager antigo — o projeto esta com activeInputHandler: 0 e nao tem
    /// com.unity.inputsystem no manifesto. Se isso mudar, muda so aqui.
    /// </summary>
    [RequireComponent(typeof(CapsuleMotor))]
    public class CapsuleController : MonoBehaviour
    {
        CapsuleMotor motor;
        bool jumpQueued;

        void Awake()
        {
            motor = GetComponent<CapsuleMotor>();
        }

        void Update()
        {
            // O pulo e lido aqui e guardado: GetButtonDown pode acontecer inteiro entre
            // dois FixedUpdate e desaparecer se so o passo de fisica olhar para ele.
            if (Input.GetButtonDown("Jump"))
            {
                jumpQueued = true;
            }
        }

        void FixedUpdate()
        {
            var intent = new MoveIntent
            {
                Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
                JumpQueued = jumpQueued,
            };

            jumpQueued = false;

            motor.Step(intent, Time.fixedDeltaTime);
        }
    }
}
