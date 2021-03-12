#include "hotstorage_model.pb.h"
//#include "heuristic.h"
#include <optional>

namespace DynStacking {
    namespace HotStorage {
        using namespace DataModel;

        /// If any block on top of a stack can be moved to the handover schedule this move.
        void any_handover_move(World& world, CraneSchedule& schedule) {
            if (!world.handover().ready()) {
                return;
            }
            for (auto& stack : world.buffers()) {
                int size = stack.bottomtotop().size();
                if (size == 0) {
                    continue;
                }
                auto& top = stack.bottomtotop(size - 1);
                if (top.ready()) {
                    auto move = schedule.add_moves();
                    move->set_blockid(top.id());
                    move->set_sourceid(stack.id());
                    move->set_targetid(world.handover().id());
                    return;
                }
            }
        }

        /// If the top block of the production stack can be put on a buffer schedule this move.
        void clear_production_stack(World& world, CraneSchedule& schedule) {
            auto& src = world.production();
            int size = src.bottomtotop_size();
            if (size == 0) {
                return;
            }
            auto& top = src.bottomtotop(size - 1);
            auto tgt = std::find_if(world.buffers().begin(), world.buffers().end(), [](auto& s) { return s.maxheight() > s.bottomtotop_size(); });
            if (tgt != world.buffers().end()) {
                auto move = schedule.add_moves();
                move->set_blockid(top.id());
                move->set_sourceid(src.id());
                move->set_targetid(tgt->id());

            }
        }

        std::optional<CraneSchedule> plan_moves(World& world) {
            if (world.crane().schedule().moves_size() > 0) {
                // Leave the existing schedule alone
                return {};
            }
            CraneSchedule schedule;
            any_handover_move(world, schedule);
            clear_production_stack(world, schedule);

            if (schedule.moves_size() > 0) {
                auto sequence = world.crane().schedule().sequencenr();
                schedule.set_sequencenr(sequence + 1);
                std::cout << schedule.DebugString() << std::endl;
                return schedule;
            }
            else {
                return {};
            }

        }
        std::optional<std::string> calculate_answer(void* world_data, size_t len) {
            World world;
            world.ParseFromArray(world_data, len);
            auto plan = plan_moves(world);
            if (plan.has_value()) {
                return plan.value().SerializeAsString();
            }
            else {
                return {};
            }
        }
    }
}