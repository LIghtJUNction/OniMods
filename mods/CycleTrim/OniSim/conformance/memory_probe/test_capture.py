import copy
import unittest

from capture import validate_observation


class ObservationTests(unittest.TestCase):
    def setUp(self):
        self.request = dict(request=1, pid=12345, identity='instance-a', runNonce='run-a')
        self.status = dict(paused=True, activeSavePath='/private/test.sav', simulationSeconds=50.0)
        self.data = dict(self.request, complete=True, **self.status,
                         unityAllocatedBytes=100, unityReservedBytes=200,
                         unityUnusedReservedBytes=100, monoUsedBytes=30,
                         monoHeapBytes=50, managedReportedBytes=30,
                         gcMaxGeneration=2, gcCollections=[1, 2, 3],
                         processMemoryKiB=dict(Rss=100, Pss=90, Swap=10, SwapPss=9, Anonymous=80))

    def test_same_paused_run_is_accepted(self):
        validate_observation(self.data, self.request, self.status)

    def test_failed_or_cross_run_observations_are_rejected(self):
        for key, value in [('complete', False), ('request', 2), ('request', True),
                           ('pid', 54321), ('identity', 'instance-b'), ('runNonce', 'run-b'),
                           ('simulationSeconds', 51.0), ('simulationSeconds', float('nan')),
                           ('activeSavePath', '/private/other.sav'), ('paused', False)]:
            with self.subTest(key=key, value=value):
                data = dict(self.data, **{key: value})
                with self.assertRaises(RuntimeError):
                    validate_observation(data, self.request, self.status)

    def test_missing_negative_or_boolean_memory_is_rejected(self):
        for key in ('unityAllocatedBytes', 'unityReservedBytes', 'unityUnusedReservedBytes',
                    'monoUsedBytes', 'monoHeapBytes', 'managedReportedBytes'):
            for value in (None, -1, True):
                with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                    validate_observation(dict(self.data, **{key: value}), self.request, self.status)
        for key in self.data['processMemoryKiB']:
            data = copy.deepcopy(self.data)
            del data['processMemoryKiB'][key]
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                validate_observation(data, self.request, self.status)

    def test_bad_gc_counters_are_rejected(self):
        for counts in ([], [1, 2], [1, 2, -1], [True, 2, 3]):
            with self.subTest(counts=counts), self.assertRaises(RuntimeError):
                validate_observation(dict(self.data, gcCollections=counts), self.request, self.status)

    def test_runtime_generation_count_is_preserved(self):
        validate_observation(dict(self.data, gcMaxGeneration=1, gcCollections=[1, 2]),
                             self.request, self.status)
        for value in (-1, True, 9):
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                validate_observation(dict(self.data, gcMaxGeneration=value), self.request, self.status)


if __name__ == '__main__':
    unittest.main()
