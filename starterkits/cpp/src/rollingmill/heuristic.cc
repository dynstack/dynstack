#include "rollingmill_model.pb.h"
//#include "heuristic.h"
#include <optional>

namespace DynStacking {
    namespace RollingMill {
        using namespace DataModel;

        int remaining_capacity(const Location& location) {
            return location.maxheight() - location.stack().bottomtotop_size();
        }
        bool is_buffer(const Location& location) {
            switch (location.type()) {
            case StackTypes::SortedBuffer:
            case StackTypes::ShuffleBuffer:
                return true;
            default:
                return false;
            }
        }

        struct Requested {
            int source_id;
            int from_top;
            int could_take_top_n;
            int target_id;
        };

        void plan_handover_crane(const World& world, PlannedCraneMoves& plan) {
            int move_id = plan.moves_size();
            std::vector<Requested> requested;
            for (auto& req : world.moverequests()) {
                for (auto& src : world.locations()) {
                    auto& blocks = src.stack().bottomtotop();
                    auto block = std::find_if(blocks.begin(), blocks.end(), [&](auto& block) {return block.id() == req.blockid(); });
                    if (block != blocks.end()) {
                        int from_top = std::distance(block, blocks.end());
                        auto ty = block->type();
                        auto seq = block->sequence();
                        auto could_take_top_n = 0;
                        for (auto b = blocks.rbegin(); b != blocks.rend(); ++b) {
                            if (b->type() == ty && b->sequence() == seq) {
                                seq++;
                                could_take_top_n++;
                            }
                            else {
                                break;
                            }
                        }

                        requested.push_back({src.id(), from_top, could_take_top_n, req.targetlocationid() });
                    }
                }
            }
            // try blocks with few others on top first
            std::sort(requested.begin(), requested.end(), [](auto& a, auto& b) {return a.from_top < b.from_top; });

            for (auto req: requested) {                
                auto move = plan.add_moves();
                move_id++;
                move->set_id(move_id);
                move->set_type(MoveType::PickupAndDropoff);
                move->mutable_releasetime()->set_milliseconds(world.now().milliseconds());
                move->set_pickuplocationid(req.source_id);
                if (req.could_take_top_n > 0) {
                    int amount = std::min(req.could_take_top_n, world.handovercrane().cranecapacity());
                    move->set_dropofflocationid(req.target_id);
                    move->set_requiredcraneid(world.handovercrane().id());
                    move->set_amount(amount);
                }
                else {
                    // Relocate blocks that are in the way
                    auto must_relocate = req.from_top - 1;
                    auto amount = std::min(must_relocate, world.handovercrane().cranecapacity());
                    auto not_found = world.locations().end();
                    auto tgt = std::find_if(world.locations().begin(), not_found, [&](auto tgt) {return is_buffer(tgt) && tgt.id()!=req.source_id && remaining_capacity(tgt) >= amount; });
                    if (tgt != not_found) {
                        move->set_dropofflocationid(tgt->id());
                        move->set_requiredcraneid(world.handovercrane().id());
                        move->set_amount(amount);
                    } else {
                        continue;
                    }
                }
                return;
            }
        }
        
        void plan_shuffle_crane(const World& world, PlannedCraneMoves& plan) {
            std::set<int> dont_use;
            for (auto move : plan.moves()) {
                dont_use.insert(move.pickuplocationid());
                dont_use.insert(move.dropofflocationid());
            }
            auto move_id = plan.moves_size();

            Location src;
            int min_sequence = INT_MAX;
            for (auto loc : world.locations()) {
                if (loc.type() != StackTypes::ArrivalStack) continue;
                for (auto block : loc.stack().bottomtotop()) {
                    if (block.sequence() < min_sequence) {
                        src = loc;
                        min_sequence = block.sequence();
                    }
                }
            }

            int amount = std::min(src.stack().bottomtotop_size(), world.shufflecrane().cranecapacity());
            if (amount == 0) {
                return;
            }
            auto not_found = world.locations().end();
            auto tgt = std::find_if(world.locations().begin(), not_found, [&](auto tgt) {return is_buffer(tgt) && remaining_capacity(tgt) >= amount && dont_use.count(tgt.id()) == 0; });
            if (tgt != not_found) {
                auto move = plan.add_moves();
                move_id += 1;
                move->set_id(move_id);
                move->set_type(MoveType::PickupAndDropoff);
                move->mutable_releasetime()->set_milliseconds(world.now().milliseconds());
                move->set_pickuplocationid(src.id());
                move->set_dropofflocationid(tgt->id());
                move->set_requiredcraneid(world.shufflecrane().id());
                move->set_amount(amount);
                return;
            }
        }

        std::optional<PlannedCraneMoves> plan_moves(World& world) {
            if (world.cranemoves().moves_size() > 0) {
                // Leave the existing schedule alone
                return {};
            }
            PlannedCraneMoves schedule;
            auto end = world.cranemoves().moves().end();
            // in the rolling mill we got to cranes we can plan independendly.
            if (!std::any_of(world.cranemoves().moves().begin(), end, [&world](auto mov) {return mov.requiredcraneid() == world.handovercrane().id(); })) {
                plan_handover_crane(world, schedule);
            }
            if (!std::any_of(world.cranemoves().moves().begin(), end, [&world](auto mov) {return mov.requiredcraneid() == world.shufflecrane().id(); })) {
                plan_shuffle_crane(world, schedule);
            }

            if (schedule.moves_size() > 0) {
                auto sequence = world.cranemoves().sequencenr();
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