# language: pt
Funcionalidade: Saga da ordem de serviço
  A ordem muda de status quando a oficina age e quando chega o recado do orçamento, da execução ou do pagamento.

  Cenário: Caminho feliz até a entrega do veículo
    Dado uma ordem recebida do cliente "João Silva" para o veículo "ABC1D23"
    Quando o diagnóstico começa
    E o item "Troca de óleo" no valor de 150 é incluído
    E a ordem é enviada para aprovação
    Então o status da ordem é "WaitingApproval"
    E o evento "OsSentForApprovalEvent" foi publicado
    Quando o orçamento é aprovado
    Então o status da ordem é "InExecution"
    E o evento "OsExecutionStartedEvent" foi publicado
    Quando a execução na oficina é concluída
    Então o status da ordem é "Finished"
    Quando o pagamento é confirmado
    Então o status da ordem é "Delivered"

  Cenário: Cliente recusa o orçamento e a ordem é cancelada
    Dado uma ordem recebida do cliente "João Silva" para o veículo "ABC1D23"
    Quando o diagnóstico começa
    E o item "Troca de óleo" no valor de 150 é incluído
    E a ordem é enviada para aprovação
    Quando o orçamento é recusado pelo motivo "Preço alto"
    Então o status da ordem é "Canceled"

  Cenário: Falha na oficina cancela a ordem e avisa a saga
    Dado uma ordem recebida do cliente "João Silva" para o veículo "ABC1D23"
    Quando o diagnóstico começa
    E o item "Troca de óleo" no valor de 150 é incluído
    E a ordem é enviada para aprovação
    Quando o orçamento é aprovado
    E a execução falha pelo motivo "Peça indisponível"
    Então o status da ordem é "Canceled"
    E o evento "OsCancelledEvent" foi publicado
