#include <iostream>
#include <zmq.hpp>
#include <zmq_addon.hpp>

#include "hotstorage/hotstorage_model.pb.h"
#include "hotstorage/heuristic.h"
#include "rollingmill/rollingmill_model.pb.h"
#include "rollingmill/heuristic.h"

using std::cout;
using std::endl;

    /*enum OptimizerType {
        RuleBased,
        ModelBased,
    };*/

enum class Problem {
    RollingMill,
    Hotstorage,
};


int main(int argc, char* argv[]) {
    GOOGLE_PROTOBUF_VERIFY_VERSION;

    if (argc < 3) {
        cout << "dynstack ADDR ID PROBLEM";
    }
    auto addr = argv[1];
    auto sim_id = argv[2];
    auto prob = argv[3];
    auto problem = Problem::Hotstorage;
    if (std::string_view(argv[3]) == "RM") {
        problem = Problem::RollingMill;
    }

    if (argc == 4) {}
    zmq::context_t context;
    zmq::socket_t socket(context, zmq::socket_type::dealer);
    socket.set(zmq::sockopt::routing_id, sim_id);
    socket.connect(addr);
    cout << "connected to " << addr << " solving " << sim_id << endl;

    std::vector<zmq::message_t> msg;
    while (true) {
        msg.clear();
        if (!zmq::recv_multipart(socket, std::back_inserter(msg))) {
            return -1;
        }
        cout << "update" << endl;
        std::optional<std::string> answer;
        switch (problem) {
        case Problem::Hotstorage:
            answer = DynStacking::HotStorage::calculate_answer(msg[2].data(), msg[2].size());
            break;
        case Problem::RollingMill:
            answer = DynStacking::RollingMill::calculate_answer(msg[2].data(), msg[2].size());
            break;

        }
        if (answer) {
            cout << "send" << endl;
            std::array<zmq::const_buffer, 3> msg = {
                zmq::str_buffer(""),
                zmq::str_buffer("crane"),
                zmq::buffer(answer.value())
            };
            if (!zmq::send_multipart(socket, msg)) {
                return -1;
            }

        }
    }
    return 0;
}
