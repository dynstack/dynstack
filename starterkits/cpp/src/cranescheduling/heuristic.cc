#include "cranescheduling_model.pb.h"
//#include "heuristic.h"
#include <optional>

namespace DynStacking {
    namespace CraneScheduling {
        using namespace DataModel;

        int schedule_nr = 0;

        bool can_reach(Crane &crane, double girder_position) {
            return crane.minposition() <= girder_position && girder_position <= crane.maxposition();
        }

        std::optional<CraneSchedulingSolution> plan_moves(World& world) {
            int move_count = world.cranemoves_size();

            if (move_count <= 0) {
                // Leave the existing schedule alone
                return {};
            }

            CraneSchedulingSolution solution;
            CraneSchedule *schedule = solution.mutable_schedule();

            schedule->set_schedulenr(++schedule_nr);

            for (CraneMove move : world.cranemoves()) {
                int crane_id = 1 + std::abs(move.id()) % 2;
                Crane crane = world.cranes()[crane_id - 1];

                // fix crane assignment if necessary
                if (!can_reach(crane, move.pickupgirderposition()) || !can_reach(crane, move.dropoffgirderposition())) {
                    crane_id = crane_id % 2 + 1;
                }

                CraneScheduleActivity *activity = schedule->add_activities();
                activity->set_craneid(crane_id);
                activity->set_moveid(move.id());
            }

            // custom moves could be added here
            // CraneMove *customMove = solution->add_custommoves();

            return solution;
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