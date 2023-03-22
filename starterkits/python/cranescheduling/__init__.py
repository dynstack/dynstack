from cranescheduling.cranescheduling_model_pb2 import World
from cranescheduling import heuristic

sequenceNr = 0

def plan_moves(world_data):
    print("plan")
    world = World()
    world.ParseFromString(world_data)
    plan = heuristic.next_moves(world)
    if plan:
        global sequenceNr
        sequenceNr += 1
        print(sequenceNr)
        plan.Schedule.ScheduleNr = sequenceNr # only for debugging
    return plan
