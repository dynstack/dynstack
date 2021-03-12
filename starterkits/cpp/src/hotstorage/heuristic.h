#include <optional>

namespace DynStacking {
	namespace HotStorage {
		std::optional<std::string> calculate_answer(void* world_data, size_t len);
		//std::optional<DataModel::CraneSchedule> plan_moves(DataModel::World& world);
	}
}