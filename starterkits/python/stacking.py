import zmq
import sys

import hotstorage;
import rollingmill;

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("""USAGE:
        python dynstack ADDR ID PROBLEM""")
        exit(1)
    if len(sys.argv) == 4:
        print("rule based stacking")
        [_, addr, id, problem] = sys.argv
        use_heuristic = True
        is_rollingmill = problem=="RM"
    else:
        [_, addr, id, problem, _] = sys.argv
        print("model based stacking")
        use_heuristic = False
        is_rollingmill = problem=="RM"
    
    context = zmq.Context()
    socket = context.socket(zmq.DEALER)
    socket.setsockopt_string(zmq.IDENTITY, id)
    socket.connect(addr)
    print("Connected socket")

    while True:
        msg = socket.recv_multipart()
        print("recv")
        plan = None
        if is_rollingmill:
            plan = rollingmill.plan_moves(msg[2])
        else:
            plan = hotstorage.plan_moves(msg[2], use_heuristic)

        if plan:
            print("send")
            msg = plan.SerializeToString()
            socket.send_multipart([b"", b"crane", msg])
            

