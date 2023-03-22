import zmq
import sys

import hotstorage;
import rollingmill;
import cranescheduling;

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("""USAGE:
        python dynstack ADDR ID PROBLEM""")
        exit(1)
    if len(sys.argv) == 4:
        print("rule based stacking")
        [_, addr, id, problem] = sys.argv
        use_heuristic = True
    else:
        [_, addr, id, problem, _] = sys.argv
        print("model based stacking")
        use_heuristic = False
    
    if not any(problem in x for x in ["HS", "RM", "CS"]):
        print("unknown problem: {}", problem)
        exit(2)

    context = zmq.Context()
    socket = context.socket(zmq.DEALER)
    socket.setsockopt_string(zmq.IDENTITY, id)
    socket.connect(addr)
    print("Connected socket")

    while True:
        msg = socket.recv_multipart()
        print("recv")
        plan = None
        if problem == "RM":
            plan = rollingmill.plan_moves(msg[2])
        elif problem == "HS":
            plan = hotstorage.plan_moves(msg[2], use_heuristic)
        elif problem == "CS":
            plan = cranescheduling.plan_moves(msg[2])
        else:
            print("unknown problem: {}", problem)
            break

        if plan:
            print("send")
            msg = plan.SerializeToString()
            socket.send_multipart([b"", b"crane", msg])
        else:
            socket.send_multipart([b"", b"crane", b""])
            

